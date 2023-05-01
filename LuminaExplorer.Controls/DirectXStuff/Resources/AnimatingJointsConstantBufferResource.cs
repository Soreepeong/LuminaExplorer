using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using Lumina.Data.Files;
using LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter.VertexShaderInputParameters;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.Util;
using Silk.NET.Direct3D11;

namespace LuminaExplorer.Controls.DirectXStuff.Resources;

public unsafe class AnimatingJointsConstantBufferResource : DirectXObject {
    public static readonly TimeSpan AnimationFadeTime = TimeSpan.FromMilliseconds(500);

    private readonly MdlFile _mdl;
    private readonly SklbFile _sklb;

    private ConstantBufferResource<JointMatrixArray>[] _boneTableBuffers;
    private readonly Matrix4x4[] _activeJointMatrices;
    private readonly List<AnimationState> _animationStates = new();
    private readonly int[] _modelBoneIndexToSkeletonBoneIndexMapping;
    private readonly Vector3[] _scratchTranslation;
    private readonly Quaternion[] _scratchRotation;
    private readonly Vector3[] _scratchScale;

    public AnimatingJointsConstantBufferResource(
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext,
        MdlFile mdl,
        SklbFile sklbFile) {
        _boneTableBuffers = Array.Empty<ConstantBufferResource<JointMatrixArray>>();
        _activeJointMatrices = new Matrix4x4[sklbFile.Bones.Length];
        _scratchTranslation = new Vector3[sklbFile.Bones.Length];
        _scratchRotation = new Quaternion[sklbFile.Bones.Length];
        _scratchScale = new Vector3[sklbFile.Bones.Length];

        try {
            _mdl = mdl;
            _sklb = sklbFile;

            var boneNameToIndex = sklbFile.Bones
                .Select((x, i) => (x, i))
                .ToImmutableDictionary(x => x.x.Name, x => x.i);

            _modelBoneIndexToSkeletonBoneIndexMapping = _mdl.BoneNameOffsets.Select(x => boneNameToIndex.TryGetValue(
                Encoding.UTF8.GetStringNullTerminated(_mdl.Strings.AsSpan((int) x)),
                out var boneIndex)
                ? boneIndex
                : -1).ToArray();

            _boneTableBuffers = new ConstantBufferResource<JointMatrixArray>[_mdl.BoneTables.Length];
            foreach (var i in Enumerable.Range(0, _mdl.BoneTables.Length)) {
                _boneTableBuffers[i] = new(pDevice, pDeviceContext, false, JointMatrixArray.Default);
                _boneTableBuffers[i].DataPull += sender => OnDataPull(sender, i);
            }
        } catch (Exception) {
            DisposePrivate(true);
            throw;
        }
    }

    private void DisposePrivate(bool disposing) {
        _ = disposing;
        _ = SafeDispose.EnumerableAsync(ref _boneTableBuffers!);
    }

    protected override void Dispose(bool disposing) {
        DisposePrivate(disposing);
        base.Dispose(disposing);
    }

    public int BufferCount => _boneTableBuffers.Length;

    public bool HasActiveAnimation => _animationStates.Any(x => x.Speed != 0);

    public void ChangeAnimation(IAnimation animation) => _animationStates.Add(new(animation));

    public void UpdateAnimationStateAndGetBuffers(Span<nint> into) {
        UpdateAnimationState();
        for (var i = 0; i < _boneTableBuffers.Length; i++)
            into[i] = (nint) _boneTableBuffers[i].Buffer;
    }

    public void UpdateAnimationState() {
        if (!UpdateAnimationStateImpl())
            for (var i = 0; i < _activeJointMatrices.Length; i++)
                _activeJointMatrices[i] = Matrix4x4.Identity;
    }

    private bool UpdateAnimationStateImpl() {
        foreach (var b in _boneTableBuffers)
            b.EnablePull = true;

        if (!_animationStates.Any())
            return false;

        var now = Environment.TickCount64;

        // Pass 1. Resolve relative poses.
        if (_animationStates.Count == 1) {
            var t = _animationStates[0].Time;
            var anim = _animationStates[0].Animation;
            for (var j = 0; j < _activeJointMatrices.Length; j++) {
                if (anim.AffectedBoneIndices.Contains(j)) {
                    // If an animation track exists, then it will replace the bind pose completely.
                    _activeJointMatrices[j] =
                        Matrix4x4.CreateScale(anim.Scale(j).Interpolate(t)) *
                        Matrix4x4.CreateFromQuaternion(anim.Rotation(j).Interpolate(t)) *
                        Matrix4x4.CreateTranslation(anim.Translation(j).Interpolate(t));
                } else
                    _activeJointMatrices[j] = _sklb.Bones[j].BindPoseRelative;
            }
            
        } else {
            Span<float> weights = stackalloc float[_animationStates.Count];
            var startIndex = _animationStates.Count - 1;
            for (; startIndex >= 0; startIndex--) {
                weights[startIndex] = (float) Math.Clamp(
                    (now -_animationStates[startIndex].BlendStartTime) / AnimationFadeTime.TotalMilliseconds, 0, 1);
                if (weights[startIndex] >= 1)
                    break;
            }

            if (startIndex < 0)
                startIndex = 0;

            for (var j = 0; j < _activeJointMatrices.Length; j++) {
                _scratchScale[j] = _sklb.Bones[j].Scale;
                _scratchRotation[j] = _sklb.Bones[j].Rotation;
                _scratchTranslation[j] = _sklb.Bones[j].Translation;
            }

            foreach (var state in _animationStates.Skip(startIndex)) {
                var t = state.Time;
                var anim = state.Animation;
                var weight = (float) Math.Clamp((now - state.BlendStartTime) / AnimationFadeTime.TotalMilliseconds, 0, 1);
                for (var j = 0; j < _activeJointMatrices.Length; j++) {
                    Quaternion rotation;
                    Vector3 scale, translation;
                    if (anim.AffectedBoneIndices.Contains(j)) {
                        // If an animation track exists, then it will replace the bind pose completely.
                        scale = anim.Scale(j).Interpolate(t);
                        rotation = anim.Rotation(j).Interpolate(t);
                        translation = anim.Translation(j).Interpolate(t);
                    } else {
                        scale = _sklb.Bones[j].Scale;
                        rotation = _sklb.Bones[j].Rotation;
                        translation = _sklb.Bones[j].Translation;
                    }
                    _scratchScale[j] = Vector3.Lerp(_scratchScale[j], scale, weight);
                    _scratchRotation[j] = Quaternion.Lerp(_scratchRotation[j], rotation, weight);
                    _scratchTranslation[j] = Vector3.Lerp(_scratchTranslation[j], translation, weight);
                }
            }
            
            _animationStates.RemoveRange(0, startIndex);
            
            for (var j = 0; j < _activeJointMatrices.Length; j++) {
                _activeJointMatrices[j] =
                    Matrix4x4.CreateScale(_scratchScale[j]) *
                    Matrix4x4.CreateFromQuaternion(_scratchRotation[j]) *
                    Matrix4x4.CreateTranslation(_scratchTranslation[j]);
            }
        }

        // Pass 2. Resolve absolute poses.
        for (var i = 0; i < _activeJointMatrices.Length; i++) {
            if (_sklb.Bones[i].Parent is { } parent)
                _activeJointMatrices[i] *= _activeJointMatrices[parent.Index];
        }

        // Pass 3. Make skinning matrices.
        for (var i = 0; i < _activeJointMatrices.Length; i++)
            _activeJointMatrices[i] = _sklb.Bones[i].BindPoseAbsoluteInverse * _activeJointMatrices[i];

        return true;
    }

    private void OnDataPull(ConstantBufferResource<JointMatrixArray> sender, int boneTableIndex) {
        var boneTable = _mdl.BoneTables[boneTableIndex];

        if (!_animationStates.Any())
            sender.UpdateDataOnce(JointMatrixArray.Default);
        else {
            var jma = JointMatrixArray.Default;
            for (var i = 0; i < boneTable.BoneCount; i++) {
                var boneIndex = _modelBoneIndexToSkeletonBoneIndexMapping[boneTable.BoneIndex[i]];
                if (0 <= boneIndex && boneIndex < _activeJointMatrices.Length)
                    jma[i] = Matrix4x4.Transpose(_activeJointMatrices[boneIndex]).TruncateAs3X4ToSilkValue();
            }

            sender.UpdateDataOnce(jma);
        }

        if (!HasActiveAnimation)
            sender.EnablePull = false;
    }

    private class AnimationState {
        private float _speed = 1f;

        public readonly IAnimation Animation;
        public readonly long BlendStartTime = Environment.TickCount64;

        public long BaseTickDelta;
        public long BaseTick = Environment.TickCount64;

        public AnimationState(IAnimation animation) => Animation = animation;

        public float Time => ((Environment.TickCount64 - BaseTick) * _speed + BaseTickDelta) / 1000;

        public float Speed {
            get => _speed;
            set {
                if (Equals(_speed, value))
                    return;

                BaseTickDelta = (long) ((Environment.TickCount64 - BaseTick) / _speed);
                _speed = value;
                BaseTick = Environment.TickCount64;
            }
        }
    }
}
