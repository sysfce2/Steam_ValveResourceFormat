using System.Buffers;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;

namespace GUI.Types.Renderer
{
    class HitboxSetSceneNode : SceneNode
    {
        class HitboxSetData
        {
            public Hitbox[] HitboxSet { get; init; }
            public HitboxSceneNode[] SceneNodes { get; init; }
            public int[] HitboxBoneIndexes { get; init; }
        }

        readonly AnimationController animationController;

        readonly Dictionary<string, HitboxSetData> hitboxSets = [];
        HitboxSetData currentSet;
        Skeleton skeleton => animationController.FrameCache.Skeleton;

        public HitboxSetSceneNode(Scene scene, AnimationController animationController, Dictionary<string, Hitbox[]> hitboxSets)
            : base(scene)
        {
            this.animationController = animationController;

            var boneIndexes = skeleton.Bones.Select((b, i) => (b, i))
                                            .ToDictionary(p => p.b.Name.ToLowerInvariant(), p => p.i);

            foreach (var pair in hitboxSets)
            {
                AddHitboxSet(pair.Key, pair.Value, boneIndexes);
            }
        }

        private void AddHitboxSet(string name, Hitbox[] hitboxSet, Dictionary<string, int> boneIndexes)
        {
            var sceneNodes = new HitboxSceneNode[hitboxSet.Length];
            var hitboxBoneIndexes = new int[hitboxSet.Length];

            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var hitbox = hitboxSet[i];
                sceneNodes[i] = HitboxSceneNode.Create(Scene, hitbox);

                if (string.IsNullOrEmpty(hitbox.BoneName) || !boneIndexes.TryGetValue(hitbox.BoneName.ToLowerInvariant(), out var boneIndex))
                {
                    hitboxBoneIndexes[i] = -1;
                }
                else
                {
                    hitboxBoneIndexes[i] = boneIndex;
                }
            }

            var data = new HitboxSetData
            {
                HitboxSet = hitboxSet,
                HitboxBoneIndexes = hitboxBoneIndexes,
                SceneNodes = sceneNodes
            };

            hitboxSets.Add(name, data);
        }

        public void SetHitboxSet(string set)
        {
            if (set == null)
            {
                currentSet = null;
                return;
            }

            currentSet = hitboxSets[set];
        }

        private static void UpdateHitboxSet(HitboxSetData hitboxSetData, Span<Matrix4x4> boneMatrices)
        {
            var hitboxSet = hitboxSetData.HitboxSet;
            for (var i = 0; i < hitboxSet.Length; i++)
            {
                var shape = hitboxSetData.SceneNodes[i];
                var hitbox = hitboxSet[i];
                var boneId = hitboxSetData.HitboxBoneIndexes[i];
                var targetTransform = boneId == -1 ? Matrix4x4.Identity : boneMatrices[boneId];

                if (hitbox.TranslationOnly)
                {
                    Matrix4x4.Decompose(targetTransform, out _, out _, out var translation);
                    shape.Transform = Matrix4x4.CreateTranslation(translation);
                }
                else
                {
                    shape.Transform = targetTransform;
                }
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (currentSet == null)
            {
                return;
            }

            LocalBoundingBox = new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue));

            var floatBuffer = ArrayPool<float>.Shared.Rent(skeleton.Bones.Length * 16);
            var boneMatrices = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer);
            try
            {
                animationController?.GetBoneMatrices(boneMatrices);
                UpdateHitboxSet(currentSet, boneMatrices);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(floatBuffer);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (currentSet == null || context.RenderPass != RenderPass.Translucent)
            {
                return;
            }

            GL.Disable(EnableCap.DepthTest);
            foreach (var node in currentSet.SceneNodes)
            {
                node.Render(context);
            }
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
