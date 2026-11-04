using UnityEngine;
using UnityEngine.UI;

namespace SurgingWaves.Radar
{
    public sealed class RadarDisplay : MonoBehaviour
    {
        public RadarSettings settings;
        public RadarNavigation navigation;
        public RadarContactManager contactManager;
        public RadarGraphic graphic;
        public Text status;
        public Text[] compass;

        private void LateUpdate()
        {
            if (settings == null || navigation == null) return;
            if (graphic != null) graphic.SetVerticesDirty();
            if (status != null)
                status.text = $"SONAR / {settings.range:0}m     SPD {navigation.CurrentSpeed:0.0} m/s\n"
                    + $"HDG {navigation.Heading:000}     GAS {(navigation.controls != null ? navigation.controls.Throttle01 : 0f) * 100f:0}%";
            if (compass == null || graphic == null) return;
            float radius = Mathf.Min(graphic.rectTransform.rect.width, graphic.rectTransform.rect.height) * 0.47f;
            for (int i = 0; i < compass.Length; ++i)
                if (compass[i] != null)
                    compass[i].rectTransform.anchoredPosition = RadarNavigation.Direction(i * 90f - navigation.Heading) * radius;
        }
    }
}
