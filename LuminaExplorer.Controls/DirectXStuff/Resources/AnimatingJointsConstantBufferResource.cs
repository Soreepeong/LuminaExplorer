using System;
using System.Collections.Immutable;
using System.IO;
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
    private readonly MdlFile _mdl;
    private readonly int[] _boneParentIndices;
    private readonly int[] _modelBoneIndexToSkeletonBoneIndexMapping;
    private readonly Matrix4x4[] _bindPoseRelative;
    private readonly Matrix4x4[] _bindPoseInverse;
    private ConstantBufferResource<JointMatrixArray>[] _boneTableBuffers;

    private readonly Matrix4x4[] _activeJointMatrices;
    private IAnimation? _animation;
    private float _animationSpeed = 1;
    private long _animationBaseTick;

    public AnimatingJointsConstantBufferResource(
        ID3D11Device* pDevice,
        ID3D11DeviceContext* pDeviceContext,
        MdlFile mdl,
        SklbFile sklbFile) {
        _boneTableBuffers = Array.Empty<ConstantBufferResource<JointMatrixArray>>();

        try {
            _mdl = mdl;
            _boneParentIndices = sklbFile.Bones.Select(x => x.ParentIndex).ToArray();

            var boneNameToIndex = sklbFile.Bones
                .Select((x, i) => (x, i))
                .ToImmutableDictionary(x => x.x.Name, x => x.i);

            _modelBoneIndexToSkeletonBoneIndexMapping = _mdl.BoneNameOffsets.Select(x => {
                var nameSpan = _mdl.Strings.AsSpan((int) x);
                var nameSpanTerminator = nameSpan.IndexOf((byte) 0);
                var name = Encoding.UTF8.GetString(nameSpanTerminator == -1
                    ? nameSpan
                    : nameSpan[..nameSpanTerminator]);
                return boneNameToIndex[name];
            }).ToArray();

            _bindPoseInverse = new Matrix4x4[sklbFile.Bones.Length];
            _bindPoseRelative = new Matrix4x4[sklbFile.Bones.Length];
            _activeJointMatrices = new Matrix4x4[sklbFile.Bones.Length];
            
            var bindPose = new Matrix4x4[sklbFile.Bones.Length];
            for (var i = 0; i < sklbFile.Bones.Length; i++) {
                var parent = _boneParentIndices[i];
                bindPose[i] = parent == -1
                    ? sklbFile.Bones[i].Matrix
                    : sklbFile.Bones[i].Matrix * bindPose[parent];
                _bindPoseInverse[i] = Matrix4x4.Invert(bindPose[i], out var inverted)
                    ? inverted
                    : throw new InvalidDataException();
                _bindPoseRelative[i] = parent == -1 ? bindPose[i] : bindPose[i] * _bindPoseInverse[parent];
            }

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

    public IAnimation? Animation {
        get => _animation;
        set {
            if (_animation == value)
                return;

            _animation = value;
            _animationBaseTick = Environment.TickCount64;
            UpdateAnimationState();
        }
    }

    public long AnimationBaseTick {
        get => _animationBaseTick;
        set {
            _animationBaseTick = value;
            UpdateAnimationState();
        }
    }

    public float AnimationSpeed {
        get => _animationSpeed;
        set {
            if (Equals(_animationSpeed, value))
                return;

            var d = (Environment.TickCount64 - _animationBaseTick) / _animationSpeed;
            _animationSpeed = value;
            _animationBaseTick = Environment.TickCount64 + (long) (d * _animationSpeed);
            UpdateAnimationState();
        }
    }

    public int BufferCount => _boneTableBuffers.Length;

    public void UpdateAnimationStateAndGetBuffers(Span<nint> into) {
        UpdateAnimationState();
        for (var i = 0; i < _boneTableBuffers.Length; i++)
            into[i] = (nint) _boneTableBuffers[i].Buffer;
    }

    public void UpdateAnimationState() {
        if (_animation is null) {
            for (var i = 0; i < _activeJointMatrices.Length; i++)
                _activeJointMatrices[i] = Matrix4x4.Identity;
        } else {
            var t = (Environment.TickCount64 - _animationBaseTick) * _animationSpeed / 1000;

            // Pass 1. Resolve relative poses.
            for (var i = 0; i < _activeJointMatrices.Length; i++) {
                if (_animation.AffectedBoneIndices.Contains(i)) {
                    _activeJointMatrices[i] = 
                        Matrix4x4.CreateScale(_animation.Scale(i).Interpolate(t)) *
                        Matrix4x4.CreateFromQuaternion(_animation.Rotation(i).Interpolate(t)) *
                        Matrix4x4.CreateTranslation(_animation.Translation(i).Interpolate(t));
                } else
                    _activeJointMatrices[i] = _bindPoseRelative[i];
            }

            // Pass 2. Resolve absolute poses.
            for (var i = 0; i < _activeJointMatrices.Length; i++) {
                var parent = _boneParentIndices[i];
                if (parent != -1)
                    _activeJointMatrices[i] *= _activeJointMatrices[parent];
            }

            // Pass 3. Make skinning matrices.
            for (var i = 0; i < _activeJointMatrices.Length; i++) {
                _activeJointMatrices[i] = _bindPoseInverse[i] * _activeJointMatrices[i];
            }
        }

        foreach (var b in _boneTableBuffers)
            b.EnablePull = true;
    }

    private void OnDataPull(ConstantBufferResource<JointMatrixArray> sender, int boneTableIndex) {
        var boneTable = _mdl.BoneTables[boneTableIndex];

        if (_animation is null)
            sender.UpdateDataOnce(JointMatrixArray.Default);
        else {
            var jma = JointMatrixArray.Default;
            for (var i = 0; i < boneTable.BoneCount; i++) {
                var boneIndex = _modelBoneIndexToSkeletonBoneIndexMapping[boneTable.BoneIndex[i]];
                jma[i] = Matrix4x4.Transpose(_activeJointMatrices[boneIndex]).TruncateAs3X4ToSilkValue();
            }

            sender.UpdateDataOnce(jma);
        }

        if (_animation is null || _animationSpeed <= 0)
            sender.EnablePull = false;
    }
}
