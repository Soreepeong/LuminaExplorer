using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class AnimatingJointsConstantBufferResource : DirectXObject {
    public static readonly TimeSpan AnimationFadeTime = TimeSpan.FromMilliseconds(500);

    private readonly MdlFile _mdl;
    private readonly SklbFile.BoneList _boneList;

    private ConstantBufferResource<JointMatrixArray>[] _boneTableBuffers;
    private readonly Matrix4x4[] _activeJointMatrices;
    private readonly List<AnimationState> _animationStates = [];
    private readonly int[] _modelBoneIndexToSkeletonBoneIndexMapping;
    private readonly Vector3[] _scratchTranslation;
    private readonly Quaternion[] _scratchRotation;
    private readonly Vector3[] _scratchScale;

    private float _animationSpeed;

    public AnimatingJointsConstantBufferResource(
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext,
        MdlFile mdl,
        SklbFile[] sklbFiles)
    {
        this._boneList = new();
        foreach (var sklb in sklbFiles) this._boneList.AddBones(sklb.Bones);

        this._boneTableBuffers = [];
        this._activeJointMatrices = new Matrix4x4[this._boneList.Bones.Count];
        this._scratchTranslation = new Vector3[this._boneList.Bones.Count];
        this._scratchRotation = new Quaternion[this._boneList.Bones.Count];
        this._scratchScale = new Vector3[this._boneList.Bones.Count];

        try {
            this._mdl = mdl;

            this._modelBoneIndexToSkeletonBoneIndexMapping = this._mdl.BoneNameOffsets.Select(
                x => this._boneList.TryGetIndex(
                    Encoding.UTF8.GetStringNullTerminated(this._mdl.Strings.AsSpan((int) x)),
                    out var boneIndex)
                    ? boneIndex
                    : -1).ToArray();

            this._boneTableBuffers = new ConstantBufferResource<JointMatrixArray>[this._mdl.BoneTables.Length];
            foreach (var i in Enumerable.Range(0, this._mdl.BoneTables.Length)) {
                this._boneTableBuffers[i] = new(pDevice, pDeviceContext, false, JointMatrixArray.Default);
                this._boneTableBuffers[i].DataPull += sender => this.OnDataPull(sender, i);
            }
        } catch (Exception) {
            this.DisposePrivate(true);
            throw;
        }
    }

    private void DisposePrivate(bool disposing)
    {
        _ = disposing;
        _ = SafeDispose.EnumerableAsync(ref this._boneTableBuffers!);
    }

    protected override void Dispose(bool disposing)
    {
        this.DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public int BufferCount => this._boneTableBuffers.Length;

    public bool HasActiveAnimation => this._animationStates.Any(x => x.Speed != 0);

    public float AnimationSpeed {
        get => this._animationSpeed;
        set {
            this._animationSpeed = value;
            foreach (var a in this._animationStates)
                a.Speed = value;
        }
    }

    public void ChangeAnimations(IAnimation[]? animations)
    {
        var now = Environment.TickCount64;
        if (animations is null) {
            foreach (var s in this._animationStates)
                s.EndTick = (long) (now + AnimationFadeTime.TotalMilliseconds);
        } else {
            var added = animations.Where(x => this._animationStates.All(y => y.Animation != x)).ToArray();
            var removed = this._animationStates.Where(x => animations.All(y => x.Animation != y)).ToArray();
            var unchanged = this._animationStates.Where(x => animations.Any(y => x.Animation == y)).ToArray();
            this._animationStates.AddRange(added.Select(x => new AnimationState(x)));
            foreach (var x in removed.Where(x => x.EndTick == long.MaxValue))
                x.EndTick = (long) (now + AnimationFadeTime.TotalMilliseconds);
            foreach (var a in unchanged.Where(a => a.EndTick != long.MaxValue)) {
                a.BlendStartTick = now - Math.Max(a.EndTick - now, 0);
                a.EndTick = long.MaxValue;
            }

            this._animationStates.Sort((x, y) => x.EndTick.CompareTo(y.EndTick));
        }
    }

    public void UpdateAnimationStateAndGetBuffers(Span<nint> into)
    {
        this.UpdateAnimationState();
        for (var i = 0; i < this._boneTableBuffers.Length; i++)
            into[i] = (nint) this._boneTableBuffers[i].Buffer;
    }

    public void UpdateAnimationState()
    {
        if (!this.UpdateAnimationStateImpl())
            for (var i = 0; i < this._activeJointMatrices.Length; i++)
                this._activeJointMatrices[i] = Matrix4x4.Identity;
    }

    private bool UpdateAnimationStateImpl()
    {
        foreach (var b in this._boneTableBuffers)
            b.EnablePull = true;

        if (!this._animationStates.Any())
            return false;

        var now = Environment.TickCount64;

        this._animationStates.RemoveAll(x => x.EndTick <= now);
        if (!this._animationStates.Any())
            return false;

        // Pass 1. Resolve relative poses.
        if (this._animationStates.Count == 1) {
            var t = this._animationStates[0].Time;
            var anim = this._animationStates[0].Animation;
            for (var j = 0; j < this._activeJointMatrices.Length; j++) {
                if (anim.AffectedBoneIndices.Contains(j)) {
                    // If an animation track exists, then it will replace the bind pose completely.
                    this._activeJointMatrices[j] =
                        Matrix4x4.CreateScale(anim.Scale(j).Interpolate(t)) *
                        Matrix4x4.CreateFromQuaternion(anim.Rotation(j).Interpolate(t)) *
                        Matrix4x4.CreateTranslation(anim.Translation(j).Interpolate(t));
                } else
                    this._activeJointMatrices[j] = this._boneList.Bones[j].BindPoseRelative;
            }
        } else {
            var startIndex = this._animationStates.Count - 1;
            for (; startIndex >= 0; startIndex--) {
                var state = this._animationStates[startIndex];
                var weight = (float) Math.Clamp(
                    Math.Min(
                        now - state.BlendStartTick,
                        state.EndTick - now
                    ) / AnimationFadeTime.TotalMilliseconds,
                    0,
                    1);
                if (weight >= 1)
                    break;
            }

            if (startIndex < 0)
                startIndex = 0;

            for (var j = 0; j < this._activeJointMatrices.Length; j++) {
                this._scratchScale[j] = this._boneList.Bones[j].Scale;
                this._scratchRotation[j] = this._boneList.Bones[j].Rotation;
                this._scratchTranslation[j] = this._boneList.Bones[j].Translation;
            }

            foreach (var state in this._animationStates.Skip(startIndex)) {
                var t = state.Time;
                var anim = state.Animation;
                var weight = (float) Math.Clamp(
                    Math.Min(
                        now - state.BlendStartTick,
                        state.EndTick - now
                    ) / AnimationFadeTime.TotalMilliseconds,
                    0,
                    1);
                for (var j = 0; j < this._activeJointMatrices.Length; j++) {
                    Quaternion rotation;
                    Vector3 scale, translation;
                    if (anim.AffectedBoneIndices.Contains(j)) {
                        // If an animation track exists, then it will replace the bind pose completely.
                        scale = anim.Scale(j).Interpolate(t);
                        rotation = anim.Rotation(j).Interpolate(t);
                        translation = anim.Translation(j).Interpolate(t);
                    } else {
                        scale = this._boneList.Bones[j].Scale;
                        rotation = this._boneList.Bones[j].Rotation;
                        translation = this._boneList.Bones[j].Translation;
                    }

                    this._scratchScale[j] = Vector3.Lerp(this._scratchScale[j], scale, weight);
                    this._scratchRotation[j] = Quaternion.Lerp(this._scratchRotation[j], rotation, weight);
                    this._scratchTranslation[j] = Vector3.Lerp(this._scratchTranslation[j], translation, weight);
                }
            }

            this._animationStates.RemoveRange(0, startIndex);

            for (var j = 0; j < this._activeJointMatrices.Length; j++) {
                this._activeJointMatrices[j] =
                    Matrix4x4.CreateScale(this._scratchScale[j]) *
                    Matrix4x4.CreateFromQuaternion(this._scratchRotation[j]) *
                    Matrix4x4.CreateTranslation(this._scratchTranslation[j]);
            }
        }

        // Pass 2. Resolve absolute poses.
        for (var i = 0; i < this._activeJointMatrices.Length; i++) {
            if (this._boneList.Bones[i].Parent is { } parent)
                this._activeJointMatrices[i] *= this._activeJointMatrices[parent.Index];
        }

        // Pass 3. Make skinning matrices.
        for (var i = 0; i < this._activeJointMatrices.Length; i++)
            this._activeJointMatrices[i] =
                this._boneList.Bones[i].BindPoseAbsoluteInverse * this._activeJointMatrices[i];

        return true;
    }

    /// <summary>
    /// Gets the skinning matrix (from the bind pose to the current pose, in the model space) of a joint referred by a
    /// bone table, as of the last call to <see cref="UpdateAnimationState"/>.
    /// </summary>
    /// <param name="boneTableIndex">Index of the bone table in the model.</param>
    /// <param name="jointIndex">Index of the joint in the bone table.</param>
    /// <returns>The skinning matrix, or identity if the joint could not be resolved.</returns>
    public Matrix4x4 GetBoneTableJointMatrix(int boneTableIndex, int jointIndex)
    {
        if (!this._animationStates.Any())
            return Matrix4x4.Identity;
        if (boneTableIndex < 0 || boneTableIndex >= this._mdl.BoneTables.Length)
            return Matrix4x4.Identity;

        var boneTable = this._mdl.BoneTables[boneTableIndex];
        if (jointIndex < 0 || jointIndex >= boneTable.BoneCount)
            return Matrix4x4.Identity;

        var modelBoneIndex = boneTable.BoneIndex[jointIndex];
        if (modelBoneIndex >= this._modelBoneIndexToSkeletonBoneIndexMapping.Length)
            return Matrix4x4.Identity;

        var boneIndex = this._modelBoneIndexToSkeletonBoneIndexMapping[modelBoneIndex];
        return 0 <= boneIndex && boneIndex < this._activeJointMatrices.Length
            ? this._activeJointMatrices[boneIndex]
            : Matrix4x4.Identity;
    }

    private void OnDataPull(ConstantBufferResource<JointMatrixArray> sender, int boneTableIndex)
    {
        var boneTable = this._mdl.BoneTables[boneTableIndex];

        if (!this._animationStates.Any())
            sender.UpdateDataOnce(JointMatrixArray.Default);
        else {
            var jma = JointMatrixArray.Default;
            for (var i = 0; i < boneTable.BoneCount; i++) {
                var boneIndex = this._modelBoneIndexToSkeletonBoneIndexMapping[boneTable.BoneIndex[i]];
                if (0 <= boneIndex && boneIndex < this._activeJointMatrices.Length)
                    jma[i] = Matrix4x4.Transpose(this._activeJointMatrices[boneIndex]).TruncateAs3X4ToSilkValue();
            }

            sender.UpdateDataOnce(jma);
        }

        if (!this.HasActiveAnimation)
            sender.EnablePull = false;
    }

    private class AnimationState {
        private float _speed = 1f;

        public readonly IAnimation Animation;
        public long BlendStartTick = Environment.TickCount64;

        public float TimeDelta;
        public long BaseTick = Environment.TickCount64;
        public long EndTick = long.MaxValue;

        public AnimationState(IAnimation animation) => this.Animation = animation;

        public float Time =>
            this.Animation.Duration == 0
                ? 0
                : ((Environment.TickCount64 - this.BaseTick) * this._speed / 1000 + this.TimeDelta) %
                this.Animation.Duration;

        public float Speed {
            get => this._speed;
            set {
                if (Equals(this._speed, value))
                    return;

                this.TimeDelta = this.Time;
                this.BaseTick = Environment.TickCount64;
                this._speed = value;
            }
        }
    }
}
