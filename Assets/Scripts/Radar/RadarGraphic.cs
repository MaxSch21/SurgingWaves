using UnityEngine;
using UnityEngine.UI;

namespace SurgingWaves.Radar
{
    /// <summary>Code drawn World Space UI; no textures, extra camera or fish GameObjects.</summary>
    public sealed class RadarGraphic : MaskableGraphic
    {
        public RadarDisplay display;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (display == null || display.settings == null) return;
            RadarSettings s = display.settings;
            Vector2 centre = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.43f;
            Rect rect = rectTransform.rect;
            Quad(mesh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), s.background);
            for (int ring = 1; ring <= 3; ++ring)
                Ring(mesh, centre, radius * ring / 3f, ring == 3 ? 2f : 1f, ring == 3 ? s.signal : s.grid);
            for (int i = 0; i < 4; ++i)
            {
                Vector2 direction = RadarNavigation.Direction(i * 45f) * radius;
                Line(mesh, centre - direction, centre + direction, 1f, s.grid);
            }
            float sweep = Time.time * s.sweepDegreesPerSecond;
            for (int i = 30; i >= 0; --i)
            {
                Color tint = s.signal; tint.a = (1f - i / 31f) * 0.07f;
                Triangle(mesh, centre, centre + RadarNavigation.Direction(sweep - i) * radius,
                    centre + RadarNavigation.Direction(sweep - i - 1f) * radius, tint);
            }
            Line(mesh, centre, centre + RadarNavigation.Direction(sweep) * radius, 1.5f, s.signal);
            if (display.navigation != null && display.contactManager != null)
            {
                float range = Mathf.Max(1f, s.range);
                foreach (RadarContact contact in display.contactManager.Contacts)
                {
                    if (!contact.Active) continue;
                    Vector2 local = display.navigation.WorldToRadar(contact.Position);
                    if (local.sqrMagnitude > range * range) continue;
                    Vector2 point = centre + local / range * (radius - 7f);
                    if (contact.Type == RadarContactType.Fish)
                        Disc(mesh, point, 4f, s.signal);
                    else
                    {
                        Vector2 up = Vector2.up * 7f, right = Vector2.right * 7f;
                        Line(mesh, point + up, point + right, 2f, s.monster);
                        Line(mesh, point + right, point - up, 2f, s.monster);
                        Line(mesh, point - up, point - right, 2f, s.monster);
                        Line(mesh, point - right, point + up, 2f, s.monster);
                    }
                }
            }
            Triangle(mesh, centre + new Vector2(0f, 14f), centre + new Vector2(-9f, -11f), centre, s.signal);
            Triangle(mesh, centre + new Vector2(0f, 14f), centre, centre + new Vector2(9f, -11f), s.signal);
        }

        private static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero); mesh.AddVert(c, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
        }
        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            Triangle(mesh, a, b, c, tint); Triangle(mesh, a, c, d, tint);
        }
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = (b - a).normalized;
            Vector2 side = new Vector2(-delta.y, delta.x) * width * 0.5f;
            Quad(mesh, a - side, b - side, b + side, a + side, tint);
        }
        private static void Ring(VertexHelper mesh, Vector2 centre, float radius, float width, Color tint)
        {
            for (int i = 0; i < 96; ++i)
                Line(mesh, centre + RadarNavigation.Direction(i * 3.75f) * radius,
                    centre + RadarNavigation.Direction((i + 1) * 3.75f) * radius, width, tint);
        }
        private static void Disc(VertexHelper mesh, Vector2 centre, float radius, Color tint)
        {
            for (int i = 0; i < 12; ++i)
                Triangle(mesh, centre, centre + RadarNavigation.Direction(i * 30f) * radius,
                    centre + RadarNavigation.Direction((i + 1) * 30f) * radius, tint);
        }
    }
}
