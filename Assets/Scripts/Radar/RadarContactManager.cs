using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SurgingWaves.Radar
{
    public enum RadarContactType { Fish, Monster }

    public sealed class RadarContact
    {
        public RadarContactType Type { get; }
        public Vector2 Position { get; set; }
        public bool Active { get; set; } = true;
        public RadarContact(RadarContactType type, Vector2 position) { Type = type; Position = position; }
    }

    public sealed class RadarContactManager : MonoBehaviour
    {
        public RadarNavigation navigation;
        public RadarSettings settings;
        public UnityEvent onFishReached = new UnityEvent();
        public UnityEvent onMonsterReached = new UnityEvent();
        [SerializeField] private float monsterDistance = 75f;
        [SerializeField] private float monsterBearing = -15f;
        private readonly List<RadarContact> contacts = new List<RadarContact>();
        public IReadOnlyList<RadarContact> Contacts => contacts;
        public int FishReached { get; private set; }
        public int MonstersReached { get; private set; }

        private void Start()
        {
            if (navigation == null || settings == null) return;
            for (int i = 0; i < settings.fishCount; ++i)
                contacts.Add(new RadarContact(RadarContactType.Fish, NewFishPosition()));
        }

        private void Update() => UpdateContacts();

        public void UpdateContacts()
        {
            if (navigation == null || settings == null) return;
            bool fishAhead = false;
            RadarContact candidate = null;
            float farthest = -1f;
            // Callbacks may add contacts. Process only those present at frame start.
            int count = contacts.Count;
            for (int i = 0; i < count; ++i)
            {
                RadarContact contact = contacts[i];
                if (!contact.Active) continue;
                float distance = Vector2.Distance(navigation.Position, contact.Position);
                float reach = Mathf.Min(settings.reachDistance, settings.range * 0.2f);
                if (contact.Type == RadarContactType.Monster)
                {
                    if (distance <= reach)
                    {
                        contact.Active = false;
                        ++MonstersReached;
                        onMonsterReached.Invoke();
                    }
                    continue;
                }
                if (distance <= reach)
                {
                    contact.Position = NewFishPosition();
                    ++FishReached;
                    onFishReached.Invoke();
                }
                else if (distance > settings.range * 1.2f)
                    contact.Position = NewFishPosition();

                Vector2 offset = contact.Position - navigation.Position;
                distance = offset.magnitude;
                if (distance > reach && distance < settings.range &&
                    Vector2.Dot(offset.normalized, RadarNavigation.Direction(navigation.Heading)) > 0.4f)
                    fishAhead = true;
                if (distance > farthest) { farthest = distance; candidate = contact; }
            }
            if (!fishAhead && candidate != null)
                candidate.Position = navigation.Position + RadarNavigation.Direction(navigation.Heading)
                    * Mathf.Max(settings.range * 0.5f, settings.reachDistance * 1.1f);
        }

        private Vector2 NewFishPosition()
        {
            float near = Mathf.Clamp(settings.spawnNear, 0.25f, 0.95f);
            float far = Mathf.Clamp(settings.spawnFar, near, 0.95f);
            return navigation.Position + RadarNavigation.Direction(navigation.Heading +
                Random.Range(-settings.spawnHalfAngle, settings.spawnHalfAngle)) * Random.Range(near, far) * settings.range;
        }

        public void AddMonsterAhead(float distance, float bearing)
        {
            if (navigation == null) return;
            Vector2 position = navigation.Position + RadarNavigation.Direction(navigation.Heading + bearing) * Mathf.Max(0f, distance);
            // Reuse inactive story contacts rather than growing the list indefinitely.
            RadarContact reusable = contacts.Find(c => c.Type == RadarContactType.Monster && !c.Active);
            if (reusable != null) { reusable.Position = position; reusable.Active = true; }
            else contacts.Add(new RadarContact(RadarContactType.Monster, position));
        }

        [ContextMenu("Spawn Monster Ahead")]
        public void SpawnMonsterAhead() => AddMonsterAhead(monsterDistance, monsterBearing);
    }
}
