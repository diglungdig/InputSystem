using System;
using UnityEngine.Experimental.Input.Utilities;
using UnityEngine.Profiling;

////TODO: add a serialized form of this and take it across domain reloads

////TODO: remove direct references to InputManager

////REVIEW: can we have a global array of InputControls?

////REVIEW: XmlDoc syntax is totally stupid in its verbosity; is there a more compact, alternative syntax we can use?

// There's two possible modes that this can be driven in:
// 1) We perform phase progression directly from state monitor callbacks
// 2) We perform phase progression delayed from a series of state change events

// What if actions and action maps become more of a configuration frontend for action state processing which stands on its own?

//trace modifier state changes that result in action state changes?

//IInputActionResponse?

////REVIEW: can we move this from being per-actionmap to being per-action asset? I.e. work for a list of maps?

//allow setup where state monitor is enabled but action is disabled?

//let's say we make this a class, move IInputStateChangeMonitor over to it, and additionally allow multiple maps
//to share the same state

namespace UnityEngine.Experimental.Input
{
    /// <summary>
    /// Dynamic execution state of one or more <see cref="InputActionMap">action maps</see>.
    /// </summary>
    /// <remarks>
    /// The aim of this class is to both put all the dynamic execution state in one place as well
    /// as to organize state in tight, GC-optimized arrays. Also, by moving state out of individual
    /// <see cref="InputActionMap">action maps</see>, we can combine the state of several maps
    /// into one single object with a single set of arrays.
    /// </remarks>
    internal struct InputActionMapState
    {
        public const int kInvalidIndex = -1;

        /// <summary>
        /// List of all resolved controls.
        /// </summary>
        /// <remarks>
        /// As we don't know in advance how many controls a binding may match (if any), we bump the size of
        /// this array in increments during resolution. This means it may be end up being larger than the total
        /// number of used controls and have empty entries at the end. Use <see cref="controlCount"/> and not
        /// <c>.Length</c> to find the actual number of controls.
        ///
        /// All bound controls are included in the array regardless of whether only a partial set of actions
        /// is currently enabled. What ultimately decides whether controls get triggered or not is whether we
        /// have installed state monitors for them or not.
        /// </remarks>
        public InputControl[] controls;

        /// <summary>
        /// Array of instantiated modifier objects.
        /// </summary>
        /// <remarks>
        /// Every binding that has modifiers corresponds to a slice of this array.
        ///
        /// Indices match between this and <see cref="modifierStates"/>.
        /// </remarks>
        public IInputBindingModifier[] modifiers;

        /// <summary>
        /// Array of instantiated composite objects.
        /// </summary>
        public object[] composites;

        /// <summary>
        /// Number of controls bound by the action map.
        /// </summary>
        public int controlCount;

        public int modifierCount;

        /// <summary>
        /// State of all bindings in the action map.
        /// </summary>
        /// <remarks>
        /// This array should not require GC scanning.
        /// </remarks>
        public BindingState[] bindingStates;

        /// <summary>
        /// State of all modifiers on bindings in the action map.
        /// </summary>
        /// <remarks>
        /// Any modifier mentioned on any of the bindings gets its own execution state record
        /// in here. The modifiers for any one binding are grouped together.
        ///
        /// This array should not require GC scanning.
        /// </remarks>
        public ModifierState[] modifierStates;

        /// <summary>
        /// Trigger state of each action in the map. Indices correspond to <see cref="InputActionMap.m_Actions"/>.
        /// </summary>
        /// <remarks>
        /// This may end up being <c>null</c> if there's binding but either no associated actions
        /// or no actions that could be found.
        ///
        /// This array should not require GC scanning.
        /// </remarks>
        public TriggerState[] actionStates;

        public int[] controlIndexToBindingIndex;

        /// <summary>
        /// Initialize execution state with given resolved binding information.
        /// </summary>
        /// <param name="resolver"></param>
        public void Initialize(InputBindingResolver resolver)
        {
            controlCount = resolver.controlCount;
            controls = resolver.controls;
            controlIndexToBindingIndex = resolver.controlIndexToBindingIndex;

            modifierStates = resolver.modifierStates;
            bindingStates = resolver.bindingStates;
            actionStates = resolver.actionStates;
        }

        /// <summary>
        /// Process a state change that has happened in one of the controls attached
        /// to this action map state.
        /// </summary>
        /// <param name="actionMap"></param>
        /// <param name="controlIndex">Index of the control that changed state.</param>
        /// <param name="bindingIndex">Index of the binding associated with the given control.</param>
        /// <param name="time">The timestamp associated with the state change (comes from the state change event).</param>
        public void ProcessControlValueChange(InputActionMap actionMap, int controlIndex, int bindingIndex, double time)
        {
            Debug.Assert(actionMap != null);
            Debug.Assert(controlIndex >= 0 && controlIndex < controls.Length);
            Debug.Assert(bindingIndex >= 0 && bindingIndex < bindingStates.Length);

            ////TODO: this is where we should filter out state changes that do not result in value changes

            // If we have modifiers, let them do all the processing. The precense of a modifier
            // essentially bypasses the default phase progression logic of an action.
            var modifierCount = bindingStates[bindingIndex].modifierCount;
            if (modifierCount > 0)
            {
                var context = new InputBindingModifierContext
                {
                    m_ActionMap = actionMap,
                    m_TriggerState = new TriggerState
                    {
                        time = time,
                        bindingIndex = bindingIndex,
                        controlIndex = controlIndex,
                    }
                };

                var modifierStartIndex = bindingStates[bindingIndex].modifierStartIndex;
                for (var i = 0; i < modifierCount; ++i)
                {
                    var index = modifierStartIndex + i;
                    var state = modifierStates[index];
                    var modifier = modifiers[index];

                    context.m_TriggerState.phase = state.phase;
                    context.m_TriggerState.startTime = state.startTime;
                    context.m_TriggerState.modifierIndex = index;

                    modifier.Process(ref context);
                }
            }
            else
            {
                // Default logic has no support for cancellations and won't ever go into started
                // phase. Will go from waiting straight to performed and then straight to waiting
                // again.
                //
                // Also, we perform the action on *any* value change. For buttons, this means that
                // if you use the default logic without a modifier, the action will be performed
                // both when you press and when you release the button.

                var trigger = new TriggerState
                {
                    phase = InputActionPhase.Performed,
                    controlIndex = controlIndex,
                    bindingIndex = bindingIndex,
                    modifierIndex = kInvalidIndex,
                    time = time,
                    startTime = time
                };
                ChangePhaseOfAction(InputActionPhase.Performed, ref trigger, actionMap);
            }
        }

        public void ProcessTimeout(InputActionMap actionMap, double time, int controlIndex, int bindingIndex, int modifierIndex)
        {
            Debug.Assert(controlIndex >= 0 && controlIndex < controlCount);
            Debug.Assert(bindingIndex >= 0 && bindingIndex < bindingStates.Length);
            Debug.Assert(modifierIndex >= 0 && modifierIndex < modifierCount);

            var currentState = modifierStates[modifierIndex];

            var context = new InputBindingModifierContext
            {
                m_ActionMap = actionMap,
                m_TriggerState =
                    new TriggerState
                {
                    phase = currentState.phase,
                    time = time,
                    controlIndex = controlIndex,
                    bindingIndex = bindingIndex,
                    modifierIndex = modifierIndex
                },
                timerHasExpired = true,
            };

            currentState.isTimerRunning = false;
            modifierStates[modifierIndex] = currentState;

            // Let modifier handle timer expiration.
            modifiers[modifierIndex].Process(ref context);
        }

        public void StartTimeout(InputActionMap actionMap, float seconds, int controlIndex, int bindingIndex,
            int modifierIndex)
        {
        }

        /// <summary>
        /// Perform a phase change on the given modifier. Only visible to observers
        /// if it happens to change the phase of the action, too.
        /// </summary>
        /// <param name="newPhase"></param>
        /// <param name="trigger"></param>
        /// <param name="actionMap"></param>
        /// <remarks>
        /// Multiple modifiers on the same binding can be started concurrently but the
        /// first modifier that starts will get to drive an action until it either cancels
        /// or performs the action.
        ///
        /// If a modifier driving an action performs it, all modifiers will reset and
        /// go back waiting.
        ///
        /// If a modifier driving an action cancels it, the next modifier in the list which
        /// has already started will get to drive the action (example: a TapModifier and a
        /// SlowTapModifier both start and the TapModifier gets to drive the action because
        /// it comes first; then the TapModifier cancels because the button is held for too
        /// long and the SlowTapModifier will get to drive the action next).
        /// </remarks>
        internal void ChangePhaseOfModifier(InputActionPhase newPhase, ref TriggerState trigger, InputActionMap actionMap)
        {
            var modifierIndex = trigger.modifierIndex;
            var bindingIndex = trigger.bindingIndex;

            Debug.Assert(modifierIndex >= 0 && modifierIndex < modifierCount);
            Debug.Assert(bindingIndex >= 0 && bindingIndex < bindingStates.Length);

            ////TODO: need to make sure that performed and cancelled phase changes happen on the *same* binding&control
            ////      as the start of the phase

            // Update modifier state.
            ThrowIfPhaseTransitionIsInvalid(modifierStates[modifierIndex].phase, newPhase, bindingIndex,
                modifierIndex, actionMap);
            modifierStates[modifierIndex].phase = newPhase;
            modifierStates[modifierIndex].triggerControlIndex = trigger.controlIndex;
            if (newPhase == InputActionPhase.Started)
                modifierStates[modifierIndex].startTime = trigger.time;

            ////REVIEW: If we want to defer triggering of actions, this is the point where we probably need to cut things off
            // See if it affects the phase of an associated action.
            var actionIndex = bindingStates[bindingIndex].actionIndex; // We already had to tap this array and entry in ProcessControlValueChange.
            if (actionIndex != -1)
            {
                if (actionStates[actionIndex].phase == InputActionPhase.Waiting)
                {
                    // We're the first modifier to go to the start phase.
                    ChangePhaseOfAction(newPhase, ref trigger, actionMap);
                }
                else if (newPhase == InputActionPhase.Cancelled && actionStates[actionIndex].modifierIndex == trigger.modifierIndex)
                {
                    // We're cancelling but maybe there's another modifier ready
                    // to go into start phase.

                    ChangePhaseOfAction(newPhase, ref trigger, actionMap);

                    var modifierStartIndex = bindingStates[bindingIndex].modifierStartIndex;
                    var numModifiers = bindingStates[bindingIndex].modifierCount;
                    for (var i = 0; i < numModifiers; ++i)
                    {
                        var index = modifierStartIndex + i;
                        if (index != trigger.modifierIndex && modifierStates[index].phase == InputActionPhase.Started)
                        {
                            var triggerForModifier = new TriggerState
                            {
                                phase = InputActionPhase.Started,
                                controlIndex = modifierStates[index].triggerControlIndex,
                                bindingIndex = trigger.bindingIndex,
                                modifierIndex = index,
                                time = trigger.time,
                                startTime = modifierStates[index].startTime
                            };
                            ChangePhaseOfAction(InputActionPhase.Started, ref triggerForModifier, actionMap);
                            break;
                        }
                    }
                }
                else if (actionStates[actionIndex].modifierIndex == trigger.modifierIndex)
                {
                    // Any other phase change goes to action if we're the modifier driving
                    // the current phase.
                    ChangePhaseOfAction(newPhase, ref trigger, actionMap);

                    // We're the modifier driving the action and we performed the action,
                    // so reset any other modifier to waiting state.
                    if (newPhase == InputActionPhase.Performed)
                    {
                        var modifierStartIndex = bindingStates[bindingIndex].modifierStartIndex;
                        var numModifiers = bindingStates[bindingIndex].modifierCount;
                        for (var i = 0; i < numModifiers; ++i)
                        {
                            var index = modifierStartIndex + i;
                            if (index != trigger.modifierIndex)
                                ResetModifier(trigger.bindingIndex, index, actionMap);
                        }
                    }
                }
            }

            // If the modifier performed or cancelled, go back to waiting.
            if (newPhase == InputActionPhase.Performed || newPhase == InputActionPhase.Cancelled)
                ResetModifier(trigger.bindingIndex, trigger.modifierIndex, actionMap);
            ////TODO: reset entire chain
        }

        // Perform a phase change on the action. Visible to observers.
        internal void ChangePhaseOfAction(InputActionPhase newPhase, ref TriggerState trigger, InputActionMap actionMap)
        {
            Debug.Assert(trigger.controlIndex >= 0 && trigger.controlIndex < controlCount);
            Debug.Assert(trigger.bindingIndex >= 0 && trigger.bindingIndex < bindingStates.Length);

            var actionIndex = bindingStates[trigger.bindingIndex].actionIndex;
            if (actionIndex == -1)
                return; // No action associated with binding.

            // Make sure phase progression is valid.
            var currentPhase = actionStates[actionIndex].phase;
            ThrowIfPhaseTransitionIsInvalid(currentPhase, newPhase, trigger.bindingIndex, trigger.modifierIndex,
                actionMap);

            // Update action state.
            actionStates[actionIndex] = trigger;
            actionStates[actionIndex].phase = newPhase;

            // Let listeners know.
            var action = actionMap.m_Actions[actionIndex];
            switch (newPhase)
            {
                case InputActionPhase.Started:
                    CallActionListeners(action, ref action.m_OnStarted, ref trigger);
                    break;

                case InputActionPhase.Performed:
                    CallActionListeners(action, ref action.m_OnPerformed, ref trigger);
                    actionStates[actionIndex].phase = InputActionPhase.Waiting; // Go back to waiting after performing action.
                    break;

                case InputActionPhase.Cancelled:
                    CallActionListeners(action, ref action.m_OnCancelled, ref trigger);
                    actionStates[actionIndex].phase = InputActionPhase.Waiting; // Go back to waiting after cancelling action.
                    break;
            }
        }

        private void CallActionListeners(InputAction action, ref InlinedArray<InputActionListener> listeners, ref TriggerState trigger)
        {
            // If there's no listeners, don't bother with anything else.
            if (listeners.length == 0)
                return;

            // If the binding that triggered is part of a composite, fetch the composite.
            object composite = null;
            var bindingIndex = trigger.bindingIndex;
            if (bindingStates[bindingIndex].isPartOfComposite)
            {
                var compositeIndex = bindingStates[bindingIndex].compositeIndex;
                Debug.Assert(compositeIndex >= 0 && compositeIndex < composites.Length);
                composite = composites[compositeIndex];
            }

            // If we got triggered under the control of a modifier, fetch its state.
            IInputBindingModifier modifier = null;
            var startTime = 0.0;
            if (trigger.modifierIndex != -1)
            {
                modifier = modifiers[trigger.modifierIndex];
                startTime = modifierStates[trigger.modifierIndex].startTime;
            }

            // Fetch control that triggered the action.
            var controlIndex = trigger.controlIndex;
            Debug.Assert(controlIndex >= 0 && controlIndex < controlCount);
            var control = controls[controlIndex];

            // We store the relevant state directly on the context instead of looking it
            // up lazily on the action to shield the context from value changes. This prevents
            // surprises on the caller side (e.g. in tests).
            var context = new InputAction.CallbackContext
            {
                m_Action = action,
                m_Control = control,
                m_Time = trigger.time,
                m_Modifier = modifier,
                m_StartTime = startTime,
                m_Composite = composite,
            };

            Profiler.BeginSample("InputActionCallback");

            var listenerCount = listeners.length;
            for (var i = 0; i < listenerCount; ++i)
                listeners[i](context);

            Profiler.EndSample();
        }

        private void ThrowIfPhaseTransitionIsInvalid(InputActionPhase currentPhase, InputActionPhase newPhase,
            int bindingIndex, int modifierIndex, InputActionMap actionMap)
        {
            if (newPhase == InputActionPhase.Started && currentPhase != InputActionPhase.Waiting)
                throw new InvalidOperationException(
                    string.Format("Cannot go from '{0}' to '{1}'; must be '{2}' (action: {3}, modifier: {4})",
                        currentPhase, InputActionPhase.Started, InputActionPhase.Waiting,
                        GetActionForBinding(bindingIndex, actionMap), GetModifierOrNull(modifierIndex)));
            if (newPhase == InputActionPhase.Performed && currentPhase != InputActionPhase.Waiting &&
                currentPhase != InputActionPhase.Started)
                throw new InvalidOperationException(
                    string.Format("Cannot go from '{0}' to '{1}'; must be '{2}' or '{3}' (action: {4}, modifier: {5})",
                        currentPhase, InputActionPhase.Performed, InputActionPhase.Waiting, InputActionPhase.Started,
                        GetActionForBinding(bindingIndex, actionMap),
                        GetModifierOrNull(bindingIndex)));
            if (newPhase == InputActionPhase.Cancelled && currentPhase != InputActionPhase.Started)
                throw new InvalidOperationException(
                    string.Format("Cannot go from '{0}' to '{1}'; must be '{2}' (action: {3}, modifier: {4})",
                        currentPhase, InputActionPhase.Cancelled, InputActionPhase.Started,
                        GetActionForBinding(bindingIndex, actionMap), GetModifierOrNull(modifierIndex)));
        }

        private object GetActionForBinding(int bindingIndex, InputActionMap actionMap)
        {
            Debug.Assert(bindingIndex >= 0 && bindingIndex < bindingStates.Length);

            var actionIndex = bindingStates[bindingIndex].actionIndex;
            if (actionIndex == kInvalidIndex)
                return "<none>";

            return actionMap.m_Actions[actionIndex];
        }

        private IInputBindingModifier GetModifierOrNull(int modifierIndex)
        {
            if (modifierIndex == kInvalidIndex)
                return null;
            return modifiers[modifierIndex];
        }

        private void ResetModifier(int bindingIndex, int modifierIndex, InputActionMap actionMap)
        {
            Debug.Assert(modifierIndex >= 0 && modifierIndex < modifierCount);
            Debug.Assert(bindingIndex >= 0 && bindingIndex < bindingStates.Length);

            modifiers[modifierIndex].Reset();

            if (modifierStates[modifierIndex].isTimerRunning)
            {
                var controlIndex = modifierStates[modifierIndex].triggerControlIndex;
                actionMap.RemoveStateChangeTimeout(controlIndex, bindingIndex, modifierIndex);
            }

            modifierStates[modifierIndex] =
                new ModifierState
            {
                phase = InputActionPhase.Waiting
            };
        }

        /// <summary>
        /// Records the current state of a single modifier attached to a binding.
        /// Each modifier keeps track of its own trigger control and phase progression.
        /// </summary>
        internal struct ModifierState
        {
            public int triggerControlIndex;
            public Flags flags;
            public double startTime;

            [Flags]
            public enum Flags
            {
                TimerRunning = 1 << 8, // Reserve first 8 bits for phase.
            }

            public bool isTimerRunning
            {
                get { return (flags & Flags.TimerRunning) == Flags.TimerRunning; }
                set
                {
                    if (value)
                        flags |= Flags.TimerRunning;
                    else
                        flags &= ~Flags.TimerRunning;
                }
            }

            public InputActionPhase phase
            {
                // We store the phase in the low 8 bits of the flags field.
                get { return (InputActionPhase)((int)flags & 0xf); }
                set { flags = (Flags)(((uint)flags & 0xfffffff0) | (uint)value); }
            }
        }

        /// <summary>
        /// Runtime state for a single binding.
        /// </summary>
        /// <remarks>
        /// Correlated to the <see cref="InputBinding"/> it corresponds to by the index in the binding
        /// array.
        ///
        /// Keep references out of this struct. It should be blittable and require no GC scanning.
        /// </remarks>
        internal struct BindingState
        {
            [Flags]
            public enum Flags
            {
                ChainsWithNext = 1 << 0,
                EndOfChain = 1 << 1,
                PartOfComposite = 1 << 2,
            }

            /// <summary>
            /// Index into <see cref="controls"/> of first control associated with the binding.
            /// </summary>
            public int controlStartIndex;

            /// <summary>
            /// Number of controls associated with this binding.
            /// </summary>
            public int controlCount;

            /// <summary>
            /// Index into <see cref="modifierStates"/> of first modifier associated with the binding.
            /// </summary>
            public int modifierStartIndex;

            /// <summary>
            /// Number of modifiers associated with this binding.
            /// </summary>
            public int modifierCount;

            /// <summary>
            /// Index of the action being triggered by the binding (if any).
            /// </summary>
            /// <remarks>
            /// For bindings that don't trigger actions, this is <c>-1</c>.
            /// </remarks>
            public int actionIndex;

            ////REVIEW: move to separate array for better scanning performance
            /// <summary>
            /// The composite that the binding is part of (if any).
            /// </summary>
            /// <remarks>
            /// </remarks>
            public int compositeIndex;

            public Flags flags;

            public bool chainsWithNext
            {
                get { return (flags & Flags.ChainsWithNext) == Flags.ChainsWithNext; }
                set
                {
                    if (value)
                        flags |= Flags.ChainsWithNext;
                    else
                        flags &= ~Flags.ChainsWithNext;
                }
            }

            public bool isEndOfChain
            {
                get { return (flags & Flags.EndOfChain) == Flags.EndOfChain; }
                set
                {
                    if (value)
                        flags |= Flags.EndOfChain;
                    else
                        flags &= ~Flags.EndOfChain;
                }
            }

            public bool isPartOfChain
            {
                get { return chainsWithNext || isEndOfChain; }
            }

            ////TODO: remove
            public bool isPartOfComposite
            {
                get { return (flags & Flags.PartOfComposite) == Flags.PartOfComposite; }
                set
                {
                    if (value)
                        flags |= Flags.PartOfComposite;
                    else
                        flags &= ~Flags.PartOfComposite;
                }
            }
        }

        /// <summary>
        /// Information about a control value change triggering a phase change.
        /// </summary>
        public struct TriggerState
        {
            /// <summary>
            /// Phase being triggered by the control value change.
            /// </summary>
            public InputActionPhase phase;

            /// <summary>
            /// The time the binding got triggered.
            /// </summary>
            public double time;

            public double startTime;
            public int controlIndex;

            /// <summary>
            /// Index into <see cref="bindingStates"/> for the binding that triggered.
            /// </summary>
            public int bindingIndex;

            /// <summary>
            /// Index into <see cref="modifierStates"/> for the modifier that triggered.
            /// </summary>
            public int modifierIndex;
        }

        public struct CompositeBindingContext
        {
        }
    }
}
