using UnityEngine;

namespace ArchScada.DigitalTwin
{
    /// <summary>
    /// Drives a 3D digital twin object based on live SCADA tag values.
    /// Attach this to a GameObject representing a piece of equipment (valve, pump, tank, etc.).
    /// </summary>
    public class DigitalTwinController : MonoBehaviour
    {
        [Header("SCADA Tag Mapping")]
        [Tooltip("SCADA tag name that drives this object (e.g. \"TankLevel\", \"TankPump\").")]
        public string TagName = "";

        [Header("Transform Mapping")]
        [Tooltip("Which transform property to drive: None, PositionY, RotationY, ScaleY.")]
        public TransformProperty TargetProperty = TransformProperty.PositionY;

        [Tooltip("Minimum SCADA value (maps to MinTransformValue).")]
        public double MinValue = 0.0;

        [Tooltip("Maximum SCADA value (maps to MaxTransformValue).")]
        public double MaxValue = 100.0;

        [Tooltip("Minimum transform value applied.")]
        public float MinTransformValue = 0.0f;

        [Tooltip("Maximum transform value applied.")]
        public float MaxTransformValue = 10.0f;

        [Header("Options")]
        [Tooltip("Smooth the value over time (seconds). 0 = instant.")]
        public float SmoothingSeconds = 0.0f;

        private float _currentSmoothedValue;
        private bool _initialized;

        private void Start()
        {
            _currentSmoothedValue = GetTransformValue();
            _initialized = true;
        }

        private void Update()
        {
            if (!ScadaTagBridge.Instance.IsTagDefined(TagName))
                return;

            double raw = ScadaTagBridge.Instance.GetTagValue(TagName);
            float target = MapValueToTransform((float)raw);

            if (SmoothingSeconds > 0.0f)
            {
                _currentSmoothedValue = Mathf.Lerp(_currentSmoothedValue, target, Time.deltaTime / SmoothingSeconds);
            }
            else
            {
                _currentSmoothedValue = target;
            }

            ApplyTransformValue(_currentSmoothedValue);
        }

        private float MapValueToTransform(float value)
        {
            if (MaxValue == MinValue) return MinTransformValue;
            float t = Mathf.Clamp01((value - (float)MinValue) / (float)(MaxValue - MinValue));
            return Mathf.Lerp(MinTransformValue, MaxTransformValue, t);
        }

        private float GetTransformValue()
        {
            return TargetProperty switch
            {
                TransformProperty.PositionY => transform.localPosition.y,
                TransformProperty.RotationY => transform.localEulerAngles.y,
                TransformProperty.ScaleY => transform.localScale.y,
                _ => 0.0f
            };
        }

        private void ApplyTransformValue(float value)
        {
            switch (TargetProperty)
            {
                case TransformProperty.PositionY:
                    transform.localPosition = new Vector3(transform.localPosition.x, value, transform.localPosition.z);
                    break;
                case TransformProperty.RotationY:
                    transform.localEulerAngles = new Vector3(transform.localEulerAngles.x, value, transform.localEulerAngles.z);
                    break;
                case TransformProperty.ScaleY:
                    transform.localScale = new Vector3(transform.localScale.x, value, transform.localScale.z);
                    break;
            }
        }
    }

    public enum TransformProperty
    {
        None,
        PositionY,
        RotationY,
        ScaleY
    }
}