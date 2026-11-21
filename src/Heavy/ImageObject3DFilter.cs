using CoreGMIC.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoreGMIC.Heavy
{
    public class ImageObject3DFilter : FilterBase
    {
        private static readonly string[] TypeChoices =
        {
            "Plane", "Cube", "Pyramid", "Sphere", "Torus", "Gyroid", "Weird", "Cup", "Rubik"
        };

        private static readonly string[] RenderingChoices =
        {
            "Dots", "Wireframe", "Flat", "Flat-Shaded", "Gouraud", "Phong"
        };

        private readonly Built3DMeshData[] _meshCache = new Built3DMeshData[9];
        private ComputeBuffer _dotPosBuffer;
        private ComputeBuffer _dotUvBuffer;
        private int _uploadedDotType = -1;

        private CommandBuffer _cmd;

        private int _type = 1;
        private bool _matchScreen = true;
        private int _width = 1024;
        private int _height = 1024;
        private float _size = 0.5f;
        private float _xAngle = 57f;
        private float _yAngle = 41f;
        private float _zAngle = 21f;
        private float _fov = 45f;
        private float _xLight = 0f;
        private float _yLight = 0f;
        private float _zLight = -100f;
        private float _specLightness = 0.5f;
        private float _specShininess = 0.7f;
        private int _rendering = 4;
        private bool _antialiasing = true;

        public override string Id => "fx_imageobject3d";
        public override string DisplayName => "3D Image Object";
        public override string Description => "Maps the frame onto a shaded 3D object with interactive camera, lighting, and rendering modes.";
        public override FilterCategory Category => FilterCategory.Standard;

        public ImageObject3DFilter()
        {
            AddParameter(FilterParameter.Choice(
                "type",
                "Type",
                "3D object geometry.",
                TypeChoices,
                1,
                v => _type = v));

            AddParameter(FilterParameter.Bool(
                "match_screen",
                "Match Screen Size",
                "Use native screen resolution and square pixels instead of custom Width/Height.",
                true,
                v => _matchScreen = v));

            AddParameter(FilterParameter.Int(
                "width",
                "Width",
                "Internal horizontal render resolution.",
                1, 4096, 1024,
                v => _width = v,
                isVisible: () => !_matchScreen));

            AddParameter(FilterParameter.Int(
                "height",
                "Height",
                "Internal vertical render resolution.",
                1, 4096, 1024,
                v => _height = v,
                isVisible: () => !_matchScreen));

            AddParameter(FilterParameter.Float(
                "size",
                "Size",
                "Scale of the 3D object relative to the viewport.",
                0f, 3f, 0.5f,
                v => _size = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "x_angle",
                "X-Angle",
                "Rotation around the X axis in degrees.",
                0f, 360f, 57f,
                v => _xAngle = v,
                step: 1f)
                .WithAnimationFrequency(0f, 0.5f, 0.01f));

            AddParameter(FilterParameter.Float(
                "y_angle",
                "Y-Angle",
                "Rotation around the Y axis in degrees.",
                0f, 360f, 41f,
                v => _yAngle = v,
                step: 1f)
                .WithAnimationFrequency(0f, 0.5f, 0.01f));

            AddParameter(FilterParameter.Float(
                "z_angle",
                "Z-Angle",
                "Rotation around the Z axis in degrees.",
                0f, 360f, 21f,
                v => _zAngle = v,
                step: 1f)
                .WithAnimationFrequency(0f, 0.5f, 0.01f));

            AddParameter(FilterParameter.Float(
                "fov",
                "FOV",
                "Camera field of view in degrees.",
                1f, 90f, 45f,
                v => _fov = v,
                step: 1f));

            AddParameter(FilterParameter.Float(
                "x_light",
                "X-Light",
                "Light X coordinate.",
                -100f, 100f, 0f,
                v => _xLight = v,
                step: 2f));

            AddParameter(FilterParameter.Float(
                "y_light",
                "Y-Light",
                "Light Y coordinate.",
                -100f, 100f, 0f,
                v => _yLight = v,
                step: 2f));

            AddParameter(FilterParameter.Float(
                "z_light",
                "Z-Light",
                "Light Z coordinate.",
                -100f, 0f, -100f,
                v => _zLight = v,
                step: 2f));

            AddParameter(FilterParameter.Float(
                "spec_lightness",
                "Specular Lightness",
                "Intensity of specular highlights.",
                0f, 1f, 0.5f,
                v => _specLightness = v,
                step: 0.05f));

            AddParameter(FilterParameter.Float(
                "spec_shininess",
                "Specular Shininess",
                "Sharpness of specular highlights.",
                0f, 3f, 0.7f,
                v => _specShininess = v,
                step: 0.05f));

            AddParameter(FilterParameter.Choice(
                "rendering",
                "Rendering",
                "3D shading and surface rendering style.",
                RenderingChoices,
                4,
                v => _rendering = v));

            AddParameter(FilterParameter.Bool(
                "antialiasing",
                "Antialiasing",
                "Render at 2x supersampled resolution and resolve edges cleanly.",
                true,
                v => _antialiasing = v));
        }

        public override void Initialize()
        {
            if (!TryCreateMaterial("CoreGMIC/ImageObject3D"))
            {
                return;
            }

            _cmd = new CommandBuffer
            {
                name = "CoreGMIC_ImageObject3D"
            };
        }

        public override void Apply(RenderTexture source, RenderTexture destination)
        {
            int idx = Mathf.Clamp(_type, 0, _meshCache.Length - 1);
            Built3DMeshData meshData = GetOrBuildMesh(idx);
            if (meshData == null || meshData.Mesh == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            int baseW = _matchScreen ? source.width : Mathf.Clamp(_width, 1, 4096);
            int baseH = _matchScreen ? source.height : Mathf.Clamp(_height, 1, 4096);
            int ss = _antialiasing ? 2 : 1;
            int rw = Mathf.Clamp(baseW * ss, 1, 4096);
            int rh = Mathf.Clamp(baseH * ss, 1, 4096);

            RenderTexture renderRt = RenderTexture.GetTemporary(rw, rh, 24, GpuUtils.ColorFormat, RenderTextureReadWrite.Linear);
            renderRt.filterMode = FilterMode.Bilinear;
            renderRt.wrapMode = TextureWrapMode.Clamp;

            try
            {
                float maxDim = Mathf.Max(rw, rh);
                float halfFovRad = Mathf.Clamp(_fov, 1f, 90f) * 0.5f * Mathf.Deg2Rad;
                float tanHalf = Mathf.Max(1e-4f, Mathf.Tan(halfFovRad));
                float focalLen = 0.5f / tanHalf;

                float vertTanHalf = tanHalf * (rh / maxDim);
                float vertFovDeg = 2f * Mathf.Atan(vertTanHalf) * Mathf.Rad2Deg;
                float aspect = (float)rw / Mathf.Max(1f, rh);

                Matrix4x4 rotMat =
                    Matrix4x4.Rotate(Quaternion.AngleAxis(-_xAngle, Vector3.right)) *
                    Matrix4x4.Rotate(Quaternion.AngleAxis(-_yAngle, Vector3.up)) *
                    Matrix4x4.Rotate(Quaternion.AngleAxis(-_zAngle, Vector3.forward));

                Matrix4x4 projMat = GL.GetGPUProjectionMatrix(
                    Matrix4x4.Perspective(vertFovDeg, aspect, 0.02f, 200f),
                    true);

                Material.SetTexture("_MainTex", source);
                Material.SetMatrix("_RotMat", rotMat);
                Material.SetFloat("_ObjectSize", Mathf.Max(1e-4f, _size));
                Material.SetFloat("_FocalLen", focalLen);
                Material.SetVector("_LightRaw", new Vector4(_xLight, _yLight, _zLight, 0f));
                Material.SetFloat("_SpecLightness", _specLightness);
                Material.SetFloat("_SpecShininess", _specShininess);
                Material.SetFloat("_PointSizePx", _antialiasing ? 3f : 1.75f);
                Material.SetVector("_ScreenSize", new Vector4(rw, rh, 0f, 0f));

                _cmd.Clear();
                _cmd.SetRenderTarget(renderRt);
                _cmd.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 0f));
                _cmd.SetViewport(new Rect(0f, 0f, rw, rh));
                _cmd.SetViewProjectionMatrices(Matrix4x4.identity, projMat);

                if (_rendering == 0)
                {
                    EnsureDotBuffers(idx, meshData);
                    if (_dotPosBuffer != null && _dotUvBuffer != null)
                    {
                        Material.SetBuffer("_DotPosBuffer", _dotPosBuffer);
                        Material.SetBuffer("_DotUvBuffer", _dotUvBuffer);
                        _cmd.DrawProcedural(
                            Matrix4x4.identity,
                            Material,
                            0,
                            MeshTopology.Triangles,
                            6,
                            meshData.DotPositions.Length);
                    }
                }
                else
                {
                    int passIdx = Mathf.Clamp(_rendering, 1, 5);
                    _cmd.DrawMesh(meshData.Mesh, Matrix4x4.identity, Material, 0, passIdx);
                }

                Graphics.ExecuteCommandBuffer(_cmd);

                if (_antialiasing)
                {
                    Graphics.Blit(renderRt, destination, Material, 6);
                }
                else
                {
                    Graphics.Blit(renderRt, destination);
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderRt);
            }
        }

        public override void Dispose()
        {
            if (_cmd != null)
            {
                _cmd.Release();
                _cmd = null;
            }

            GpuUtils.ReleaseBuffer(ref _dotPosBuffer);
            GpuUtils.ReleaseBuffer(ref _dotUvBuffer);
            _uploadedDotType = -1;

            for (int i = 0; i < _meshCache.Length; i++)
            {
                if (_meshCache[i] != null && _meshCache[i].Mesh != null)
                {
                    Object.Destroy(_meshCache[i].Mesh);
                }
                _meshCache[i] = null;
            }

            base.Dispose();
        }

        private Built3DMeshData GetOrBuildMesh(int typeIdx)
        {
            if (_meshCache[typeIdx] == null)
            {
                _meshCache[typeIdx] = ImageObject3DMeshBuilder.Build(typeIdx);
            }
            return _meshCache[typeIdx];
        }

        private void EnsureDotBuffers(int typeIdx, Built3DMeshData data)
        {
            if (_uploadedDotType == typeIdx && _dotPosBuffer != null && _dotUvBuffer != null)
            {
                return;
            }

            GpuUtils.ReleaseBuffer(ref _dotPosBuffer);
            GpuUtils.ReleaseBuffer(ref _dotUvBuffer);

            int count = Mathf.Max(1, data.DotPositions.Length);
            _dotPosBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            _dotUvBuffer = new ComputeBuffer(count, sizeof(float) * 4);
            _dotPosBuffer.SetData(data.DotPositions);
            _dotUvBuffer.SetData(data.DotUvs);
            _uploadedDotType = typeIdx;
        }
    }
}