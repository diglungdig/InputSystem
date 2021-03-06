using UnityEngine.Experimental.Input.Controls;

namespace UnityEngine.Experimental.Input.Plugins.XR
{
    [InputControlLayout]
    public class WMRHMD : XRHMD
    {
        public IntegerControl trackingState { get; private set; }
        public ButtonControl isTracked { get; private set; }
        public Vector3Control devicePosition { get; private set; }
        public QuaternionControl deviceRotation { get; private set; }
        public Vector3Control leftEyePosition { get; private set; }
        public QuaternionControl leftEyeRotation { get; private set; }
        public Vector3Control rightEyePosition { get; private set; }
        public QuaternionControl rightEyeRotation { get; private set; }
        public Vector3Control centerEyePosition { get; private set; }
        public QuaternionControl centerEyeRotation { get; private set; }


        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            base.FinishSetup(builder);

            trackingState = builder.GetControl<IntegerControl>("trackingState");
            isTracked = builder.GetControl<ButtonControl>("isTracked");
            devicePosition = builder.GetControl<Vector3Control>("devicePosition");
            deviceRotation = builder.GetControl<QuaternionControl>("deviceRotation");
            leftEyePosition = builder.GetControl<Vector3Control>("leftEyePosition");
            leftEyeRotation = builder.GetControl<QuaternionControl>("leftEyeRotation");
            rightEyePosition = builder.GetControl<Vector3Control>("rightEyePosition");
            rightEyeRotation = builder.GetControl<QuaternionControl>("rightEyeRotation");
            centerEyePosition = builder.GetControl<Vector3Control>("centerEyePosition");
            centerEyeRotation = builder.GetControl<QuaternionControl>("centerEyeRotation");
        }
    }

    [InputControlLayout(commonUsages = new[] { "LeftHand", "RightHand" })]
    public class WMRSpatialController : XRControllerWithRumble
    {
        public AxisControl combinedTrigger { get; private set; }
        public Vector2Control joystick { get; private set; }
        public AxisControl trigger { get; private set; }
        public Vector2Control touchpad { get; private set; }
        public ButtonControl grip { get; private set; }
        public ButtonControl menu { get; private set; }
        public ButtonControl joystickClick { get; private set; }
        public ButtonControl triggerPressed { get; private set; }
        [InputControl(aliases = new[] { "touchpadClick" })]
        public ButtonControl touchpadClicked { get; private set; }
        [InputControl(aliases = new[] { "touchpadTouch" })]
        public ButtonControl touchPadTouched { get; private set; }
        public IntegerControl trackingState { get; private set; }
        public ButtonControl isTracked { get; private set; }
        public Vector3Control devicePosition { get; private set; }
        public QuaternionControl deviceRotation { get; private set; }

        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            base.FinishSetup(builder);

            combinedTrigger = builder.GetControl<AxisControl>("combinedTrigger");
            joystick = builder.GetControl<Vector2Control>("joystick");
            trigger = builder.GetControl<AxisControl>("trigger");
            touchpad = builder.GetControl<Vector2Control>("touchpad");
            grip = builder.GetControl<ButtonControl>("grip");
            menu = builder.GetControl<ButtonControl>("menu");
            joystickClick = builder.GetControl<ButtonControl>("joystickClick");
            triggerPressed = builder.GetControl<ButtonControl>("triggerPressed");
            touchpadClicked = builder.GetControl<ButtonControl>("touchpadClicked");
            touchPadTouched = builder.GetControl<ButtonControl>("touchPadTouched");
            trackingState = builder.GetControl<IntegerControl>("trackingState");
            isTracked = builder.GetControl<ButtonControl>("isTracked");
            devicePosition = builder.GetControl<Vector3Control>("devicePosition");
            deviceRotation = builder.GetControl<QuaternionControl>("deviceRotation");
        }
    }
}
