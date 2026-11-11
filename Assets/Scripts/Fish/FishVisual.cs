using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurgingWaves.Fish
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FishVisual : MonoBehaviour
    {
        [SerializeField] private FishBodySimulation body;
        [SerializeField] private GameObject sourceModel;
        [SerializeField] private Material material;
        [SerializeField, Tooltip("Direction toward the head in the imported model's root space.")]
        private Vector3 modelHeadDirection = Vector3.right;
        [SerializeField] private Vector3 modelUpDirection = Vector3.up;

        private Mesh runtimeMesh;
        private GameObject skinRoot;
        private Transform[] bones;
        private bool isEditorPreview;
#if UNITY_EDITOR
        private bool previewDirty = true;
        private int previewPointCount;
        private float previewBodyLength;

        private void OnEnable()
        {
            previewDirty = true;
        }

        private void OnValidate()
        {
            // Object creation is deferred to LateUpdate on the editor's main thread.
            previewDirty = true;
        }

        private void UpdateEditorPreview()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !gameObject.scene.IsValid()) return;
            if (body == null || sourceModel == null || material == null)
            {
                ClearVisual();
                previewDirty = true;
                return;
            }
            int count = body.PreviewPointCount;
            float length = body.PreviewBodyLength;
            if (!previewDirty && isEditorPreview && previewPointCount == count && Mathf.Approximately(previewBodyLength, length)) return;

            ClearVisual();
            previewDirty = false;
            previewPointCount = count;
            previewBodyLength = length;
            BuildSkin(count, length);
        }
#endif

        private void Start()
        {
            if (Application.IsPlaying(gameObject)) InitializeRuntimeVisual();
        }

        private void InitializeRuntimeVisual()
        {
            ClearVisual();
            if (body == null || sourceModel == null || material == null || body.PointCount < 3)
            {
                Debug.LogError("FishVisual needs a body simulation, readable source model and material.", this);
                enabled = false;
                return;
            }
            BuildSkin(body.PointCount, body.BodyLength);
        }

        private void BuildSkin(int pointCount, float bodyLength)
        {
            isEditorPreview = !Application.IsPlaying(gameObject);
            HideFlags generatedFlags = isEditorPreview ? HideFlags.HideAndDontSave : HideFlags.None;
            GameObject source = Instantiate(sourceModel, transform, false);
            source.name = "Fish source (temporary)";
            source.hideFlags = generatedFlags;
            foreach (Animator animator in source.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            // Preserve the importer's axis conversion; size is normalized below.
            source.transform.localPosition = Vector3.zero;

            List<CombineInstance> parts = new List<CombineInstance>();
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    source.SetActive(false);
                    DestroyGeneratedObject(source);
                    Debug.LogError("Enable Read/Write on the fish model importer before using FishVisual.", this);
                    if (!isEditorPreview) enabled = false;
                    return;
                }
                Matrix4x4 toBody = body.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = subMesh, transform = toBody });
            }
            source.SetActive(false);
            DestroyGeneratedObject(source);
            if (parts.Count == 0)
            {
                Debug.LogError("The fish source has no readable static meshes to bind.", this);
                if (!isEditorPreview) enabled = false;
                return;
            }

            runtimeMesh = new Mesh { name = "Fish prototype skin", indexFormat = IndexFormat.UInt32, hideFlags = generatedFlags };
            runtimeMesh.CombineMeshes(parts.ToArray(), true, true);
            Vector3[] vertices = runtimeMesh.vertices;
            Vector3[] normals = runtimeMesh.normals;
            Vector4[] tangents = runtimeMesh.tangents;
            Vector3 head = modelHeadDirection.sqrMagnitude > 0.0001f ? modelHeadDirection.normalized : Vector3.right;
            Vector3 up = Vector3.ProjectOnPlane(modelUpDirection, head);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, head);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.up, head);
            up.Normalize();
            Vector3 right = Vector3.Cross(up, head).normalized;
            Bounds bounds = runtimeMesh.bounds;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (Vector3 vertex in vertices)
            {
                float along = Vector3.Dot(vertex, head);
                minimum = Mathf.Min(minimum, along);
                maximum = Mathf.Max(maximum, along);
            }
            float scale = bodyLength / Mathf.Max(0.0001f, maximum - minimum);
            float segment = bodyLength / (pointCount - 1);
            BoneWeight[] weights = new BoneWeight[vertices.Length];
            float[] radii = new float[pointCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] - bounds.center;
                Vector3 vertex = new Vector3(Vector3.Dot(offset, right), Vector3.Dot(offset, up),
                    Vector3.Dot(vertices[i], head) - maximum) * scale;
                vertices[i] = vertex;
                if (normals.Length == vertices.Length)
                    normals[i] = new Vector3(Vector3.Dot(normals[i], right), Vector3.Dot(normals[i], up), Vector3.Dot(normals[i], head)).normalized;
                if (tangents.Length == vertices.Length)
                {
                    Vector3 tangent = tangents[i];
                    Vector3 rotated = new Vector3(Vector3.Dot(tangent, right), Vector3.Dot(tangent, up), Vector3.Dot(tangent, head)).normalized;
                    tangents[i] = new Vector4(rotated.x, rotated.y, rotated.z, tangents[i].w);
                }
                float coordinate = Mathf.Clamp(-vertex.z / segment, 0f, pointCount - 1);
                int a = Mathf.Min(Mathf.FloorToInt(coordinate), pointCount - 2), b = a + 1;
                float fraction = coordinate - a;
                weights[i] = new BoneWeight { boneIndex0 = a, boneIndex1 = b, weight0 = 1f - fraction, weight1 = fraction };
                float radius = new Vector2(vertex.x, vertex.y).magnitude;
                if (fraction < 0.999f) radii[a] = Mathf.Max(radii[a], radius);
                if (fraction > 0.001f) radii[b] = Mathf.Max(radii[b], radius);
            }
            runtimeMesh.vertices = vertices;
            if (normals.Length == vertices.Length) runtimeMesh.normals = normals;
            else runtimeMesh.RecalculateNormals();
            if (tangents.Length == vertices.Length) runtimeMesh.tangents = tangents;
            runtimeMesh.boneWeights = weights;
            runtimeMesh.RecalculateBounds();

            skinRoot = new GameObject(isEditorPreview ? "Fish Preview" : "Fish Skin") { hideFlags = generatedFlags };
            skinRoot.transform.SetParent(body.transform, false);
            skinRoot.layer = body.gameObject.layer;
            bones = new Transform[pointCount];
            Matrix4x4[] bindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = new GameObject("Spine " + i) { hideFlags = generatedFlags }.transform;
                bone.SetParent(skinRoot.transform, false);
                bone.localPosition = Vector3.back * (i * segment);
                bone.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                bones[i] = bone;
                bindPoses[i] = bone.worldToLocalMatrix * skinRoot.transform.localToWorldMatrix;
            }
            runtimeMesh.bindposes = bindPoses;
            SkinnedMeshRenderer renderer = skinRoot.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = runtimeMesh;
            renderer.sharedMaterial = material;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.quality = SkinQuality.Bone2;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = new Bounds(Vector3.back * (bodyLength * 0.5f), Vector3.one * (bodyLength * 3f));
            if (!isEditorPreview)
            {
                body.SetVisualRadii(radii);
                UpdateBones();
            }
        }

        private void LateUpdate()
        {
            if (Application.IsPlaying(gameObject))
            {
                if (bones == null || isEditorPreview) InitializeRuntimeVisual();
                if (bones != null) UpdateBones();
                return;
            }
#if UNITY_EDITOR
            UpdateEditorPreview();
#endif
        }

        private void UpdateBones()
        {
            Vector3 previousDirection = body.transform.rotation * Vector3.back;
            Vector3 up = body.transform.up;
            for (int i = 0; i < bones.Length; i++)
            {
                Vector3 point = body.GetRenderPoint(i);
                Vector3 direction = i < bones.Length - 1 ? body.GetRenderPoint(i + 1) - point
                    : point - body.GetRenderPoint(i - 1);
                if (direction.sqrMagnitude < 0.000001f) direction = previousDirection;
                direction.Normalize();
                up = Quaternion.FromToRotation(previousDirection, direction) * up;
                up = Vector3.ProjectOnPlane(up, direction);
                if (up.sqrMagnitude < 0.000001f)
                    up = Vector3.ProjectOnPlane(body.transform.right, direction);
                if (up.sqrMagnitude < 0.000001f)
                    up = Vector3.ProjectOnPlane(body.transform.forward, direction);
                up.Normalize();
                bones[i].SetPositionAndRotation(point, Quaternion.LookRotation(direction, up));
                previousDirection = direction;
            }
        }

        private void OnDisable()
        {
            ClearVisual();
        }

        private void OnDestroy()
        {
            ClearVisual();
        }

        private void ClearVisual()
        {
            // Deactivate before deferred destruction to avoid displaying two skins in a frame.
            if (skinRoot != null) skinRoot.SetActive(false);
            DestroyGeneratedObject(skinRoot);
            DestroyGeneratedObject(runtimeMesh);
            skinRoot = null;
            runtimeMesh = null;
            bones = null;
            isEditorPreview = false;
        }

        private void DestroyGeneratedObject(Object generated)
        {
            if (generated == null) return;
            if (Application.IsPlaying(gameObject)) Destroy(generated);
            else DestroyImmediate(generated);
        }
    }
}
