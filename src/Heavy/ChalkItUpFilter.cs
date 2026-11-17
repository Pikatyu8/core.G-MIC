using System;
using System.Collections.Generic;
using CoreGMIC.Core;
using UnityEngine;

namespace CoreGMIC.Heavy
{
    public class ChalkItUpFilter : IFilter
    {
        private static readonly string[] MorphoActionChoices =
        {
            "Erosion",
            "Dilation",
            "Opening",
            "Closing",
            "Orig - Erosion",
            "Dilation - Orig",
            "Orig - Opening",
            "Closing - Orig"
        };

        private static readonly string[] ChannelChoices =
        {
            "All",
            "RGBA [all]",
            "RGB [all]",
            "RGB [red]",
            "RGB [green]",
            "RGB [blue]",
            "RGBA [alpha]",
            "Linear RGB [all]",
            "Linear RGB [red]",
            "Linear RGB [green]",
            "Linear RGB [blue]",
            "YCbCr [Y]",
            "YCbCr [CbCr]",
            "YCbCr [Cb]",
            "YCbCr [Cr]",
            "YCbCr [Cg]",
            "Lab [L]",
            "Lab [ab]",
            "Lab [a]",
            "Lab [b]",
            "Lch [ch]",
            "Lch [c]",
            "Lch [h]",
            "HSV [H]",
            "HSV [S]",
            "HSV [V]",
            "HSI [I]",
            "HSL [L]",
            "CMYK [C]",
            "CMYK [M]",
            "CMYK [Y]",
            "CMYK [K]"
        };

        private static readonly int[] JfaSteps = { 64, 32, 16, 8, 4, 2, 1 };

        private Material _material;
        private readonly List<FilterParameter> _parameters = new List<FilterParameter>();

        private int _abstraction = 5;
        private float _detailsScale = 2.5f;
        private float _color = 1.5f;
        private float _smoothness = 50f;
        private bool _sharpenShades = true;

        private int _action = 5;
        private int _size = 5;
        private bool _invertColors = false;
        private bool _octagonalShape = false;
        private int _channelMorpho = 7;

        private float _xVariations = 0.8f;
        private float _yVariations = 1.9f;
        private int _channelSpread = 7;

        public string Id => "samj_chalkitup";
        public string DisplayName => "Chalk It Up";
        public string Description => "Chalk-and-pastel stylization with local normalization, JFA watershed, morphology, and spread.";
        public FilterCategory Category => FilterCategory.Heavy;
        public bool IsAvailable => _material != null;
        public IReadOnlyList<FilterParameter> Parameters => _parameters;

        public ChalkItUpFilter()
        {
            _parameters.Add(FilterParameter.Int(
                "abstraction",
                "Abstraction",
                "Iterations of local luminance normalization.",
                0, 10, 5,
                v => _abstraction = v));

            _parameters.Add(FilterParameter.Float(
                "details_scale",
                "Details Scale",
                "Anisotropic painting stroke scale and edge sharpness.",
                0f, 10f, 2.5f,
                v => _detailsScale = v));

            _parameters.Add(FilterParameter.Float(
                "color",
                "Color",
                "CIE Lab chromaticity multiplier.",
                0f, 5f, 1.5f,
                v => _color = v));

            _parameters.Add(FilterParameter.Float(
                "smoothness",
                "Smoothness",
                "Post-segmentation edge-preserving smoothness.",
                0f, 200f, 50f,
                v => _smoothness = v));

            _parameters.Add(FilterParameter.Bool(
                "sharpen_shades",
                "Sharpen Shades",
                "Apply GPU watershed color segmentation.",
                true,
                v => _sharpenShades = v));

            _parameters.Add(FilterParameter.Choice(
                "action",
                "Morpho Action",
                "Morphological outline/filter operation.",
                MorphoActionChoices,
                5,
                v => _action = v));

            _parameters.Add(FilterParameter.Int(
                "size",
                "Morpho Size",
                "Kernel size for morphological erosion/dilation.",
                0, 32, 5,
                v => _size = v));

            _parameters.Add(FilterParameter.Bool(
                "invert_colors",
                "Invert Colors",
                "Invert output after morphological operation.",
                false,
                v => _invertColors = v));

            _parameters.Add(FilterParameter.Bool(
                "shape",
                "Octagonal Shape",
                "Use octagonal structuring element instead of square.",
                false,
                v => _octagonalShape = v));

            _parameters.Add(FilterParameter.Choice(
                "channel_morpho",
                "Morpho Channel",
                "Color space/channels used for morphology.",
                ChannelChoices,
                7,
                v => _channelMorpho = v));

            _parameters.Add(FilterParameter.Float(
                "x_variations",
                "Spread X",
                "Horizontal chalk scatter radius in pixels.",
                -5f, 5f, 0.8f,
                v => _xVariations = v));

            _parameters.Add(FilterParameter.Float(
                "y_variations",
                "Spread Y",
                "Vertical chalk scatter radius in pixels.",
                -5f, 5f, 1.9f,
                v => _yVariations = v));

            _parameters.Add(FilterParameter.Choice(
                "channel_spread",
                "Spread Channel",
                "Color space/channels used for spread.",
                ChannelChoices,
                7,
                v => _channelSpread = v));
        }

        public void Initialize()
        {
            Shader shader = ShaderManager.GetShader("CoreGMIC/ChalkItUp");
            if (shader == null)
            {
                return;
            }

            _material = new Material(shader);
        }

        public void Apply(RenderTexture source, RenderTexture destination)
        {
            int w = source.width;
            int h = source.height;
            int qw = Mathf.Max(1, w / 4);
            int qh = Mathf.Max(1, h / 4);

            RenderTextureFormat colorFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            RenderTextureFormat floatFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)
                ? RenderTextureFormat.ARGBFloat
                : colorFormat;

            RenderTexture rtA = null;
            RenderTexture rtB = null;
            RenderTexture auxA = null;
            RenderTexture auxB = null;
            RenderTexture extraA = null;
            RenderTexture qA = null;
            RenderTexture qB = null;
            RenderTexture jfaA = null;
            RenderTexture jfaB = null;

            try
            {
                rtA = GetTempRt(w, h, colorFormat, FilterMode.Bilinear);
                rtB = GetTempRt(w, h, colorFormat, FilterMode.Bilinear);
                auxA = GetTempRt(w, h, floatFormat, FilterMode.Bilinear);
                auxB = GetTempRt(w, h, floatFormat, FilterMode.Bilinear);
                extraA = GetTempRt(w, h, colorFormat, FilterMode.Bilinear);

                RenderTexture curr = source;
                RenderTexture NextColorRt(RenderTexture c) => (c == rtA) ? rtB : rtA;

                // 1. fx_normalize_local loop on YCbCr luminance (channel 11).
                if (_abstraction > 0)
                {
                    qA = GetTempRt(qw, qh, colorFormat, FilterMode.Bilinear);
                    qB = GetTempRt(qw, qh, colorFormat, FilterMode.Bilinear);

                    Graphics.Blit(curr, rtA, _material, 0); // Pass 0: RgbToYCbCr
                    curr = rtA;

                    for (int iter = 0; iter < _abstraction; iter++)
                    {
                        Graphics.Blit(curr, auxA, _material, 1); // Pass 1: LocalMinMaxH (radius 6 -> 13x13)
                        Graphics.Blit(auxA, auxB, _material, 2); // Pass 2: LocalMinMaxV
                        Graphics.Blit(auxB, auxA, _material, 3); // Pass 3: BlurSigma5H
                        Graphics.Blit(auxA, auxB, _material, 4); // Pass 4: BlurSigma5V -> (E_s, D_s)

                        // Quarter-res sigma=5 blur approximates sigma=20 at full resolution.
                        Graphics.Blit(curr, qA);
                        Graphics.Blit(qA, qB, _material, 3);
                        Graphics.Blit(qB, qA, _material, 4);

                        RenderTexture next = NextColorRt(curr);
                        _material.SetTexture("_AuxTex", auxB);
                        _material.SetTexture("_AvgTex", qA);
                        Graphics.Blit(curr, next, _material, 5); // Pass 5: NormalizeLocalCombine
                        curr = next;
                    }

                    RenderTexture rgbOut = NextColorRt(curr);
                    Graphics.Blit(curr, rgbOut, _material, 6); // Pass 6: YCbCrToRgb
                    curr = rgbOut;
                }

                // 2. fx_smooth_anisotropic
                if (_detailsScale > 0.01f)
                {
                    _material.SetFloat("_Sigma", _detailsScale);
                    _material.SetFloat("_Sharpness", 100f * _detailsScale);
                    _material.SetFloat("_Anisotropy", 0.2f);
                    _material.SetFloat("_Dl", 2f * _detailsScale);

                    Graphics.Blit(curr, auxA, _material, 7); // Pass 7: StructureTensor
                    Graphics.Blit(auxA, auxB, _material, 8); // Pass 8: BlurTensorH
                    Graphics.Blit(auxB, auxA, _material, 9); // Pass 9: BlurTensorV

                    RenderTexture next = NextColorRt(curr);
                    _material.SetTexture("_TensorTex", auxA);
                    Graphics.Blit(curr, next, _material, 10); // Pass 10: AnisotropicLIC
                    curr = next;
                }

                // 3. fx_mix_lab (scale a,b by _Color and blur chroma by 0.5%)
                _material.SetFloat("_ColorMul", _color);
                Graphics.Blit(curr, auxA, _material, 11); // Pass 11: RgbToLab01
                Graphics.Blit(auxA, auxB, _material, 12); // Pass 12: ScaleAndBlurLabChromaH
                Graphics.Blit(auxB, auxA, _material, 13); // Pass 13: BlurLabChromaV
                RenderTexture labOut = NextColorRt(curr);
                Graphics.Blit(auxA, labOut, _material, 14); // Pass 14: Lab01ToRgb
                curr = labOut;

                // 4. fx_segment_watershed (GPU JFA approximation)
                if (_sharpenShades)
                {
                    jfaA = GetTempRt(w, h, floatFormat, FilterMode.Point);
                    jfaB = GetTempRt(w, h, floatFormat, FilterMode.Point);

                    Graphics.Blit(curr, auxA, _material, 15); // Pass 15: BlurColor1H
                    Graphics.Blit(auxA, auxB, _material, 16); // Pass 16: BlurColor1V (blurred image)
                    Graphics.Blit(auxB, extraA, _material, 17); // Pass 17: GradientNorm

                    Graphics.Blit(extraA, jfaA, _material, 18); // Pass 18: WatershedSeedInit
                    RenderTexture jfaSrc = jfaA;
                    RenderTexture jfaDst = jfaB;

                    _material.SetTexture("_GradTex", extraA);
                    for (int s = 0; s < JfaSteps.Length; s++)
                    {
                        _material.SetFloat("_JfaStep", JfaSteps[s]);
                        Graphics.Blit(jfaSrc, jfaDst, _material, 19); // Pass 19: WatershedJfaStep
                        jfaSrc = jfaDst;
                        jfaDst = (jfaSrc == jfaA) ? jfaB : jfaA;
                    }

                    RenderTexture next = NextColorRt(curr);
                    _material.SetTexture("_JfaTex", jfaSrc);
                    Graphics.Blit(auxB, next, _material, 20); // Pass 20: WatershedResolve
                    curr = next;
                }

                // 5. Final smooth in fx_painting
                if (_smoothness > 0.5f)
                {
                    _material.SetFloat("_Sigma", 1f);
                    _material.SetFloat("_Sharpness", _smoothness);
                    _material.SetFloat("_Anisotropy", 0f);
                    _material.SetFloat("_Dl", 1f);

                    Graphics.Blit(curr, auxA, _material, 7);
                    Graphics.Blit(auxA, auxB, _material, 8);
                    Graphics.Blit(auxB, auxA, _material, 9);

                    RenderTexture next = NextColorRt(curr);
                    _material.SetTexture("_TensorTex", auxA);
                    Graphics.Blit(curr, next, _material, 10);
                    curr = next;
                }

                // 6. ancien_gimp_morpho
                Graphics.Blit(curr, extraA); // Preserve pre-morpho sRGB
                _material.SetInt("_ChannelMode", _channelMorpho);
                Graphics.Blit(extraA, auxA, _material, 21); // Pass 21: EncodeChannelSpace -> auxA = encodedOrig

                RenderTexture morphoResult;
                bool needsSecondStage = (_action == 2 || _action == 3 || _action == 6 || _action == 7);
                bool firstIsDilate = (_action == 1 || _action == 3 || _action == 5 || _action == 7);

                if (needsSecondStage)
                {
                    RenderTexture stage1 = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                    RenderTexture freeA = (stage1 == rtA) ? rtB : rtA;
                    RenderTexture freeB = auxB;
                    morphoResult = RunMorpho(stage1, freeA, (freeA == rtA) ? rtB : rtA, freeB, !firstIsDilate);
                }
                else
                {
                    morphoResult = RunMorpho(auxA, rtA, rtB, auxB, firstIsDilate);
                }

                RenderTexture combineOut = (morphoResult == rtA) ? rtB : rtA;
                _material.SetTexture("_BaseTex", auxA);
                _material.SetInt("_MorphoAction", _action);
                _material.SetFloat("_InvertColors", _invertColors ? 1f : 0f);
                Graphics.Blit(morphoResult, combineOut, _material, 25); // Pass 25: MorphoCombineAndInvert

                RenderTexture mergedMorpho = (combineOut == rtA) ? rtB : rtA;
                _material.SetTexture("_BaseTex", extraA);
                _material.SetInt("_ChannelMode", _channelMorpho);
                Graphics.Blit(combineOut, mergedMorpho, _material, 26); // Pass 26: DecodeAndMergeChannel
                curr = mergedMorpho;

                // 7. fx_spread
                if (Mathf.Abs(_xVariations) > 0.001f || Mathf.Abs(_yVariations) > 0.001f)
                {
                    Graphics.Blit(curr, extraA);
                    _material.SetInt("_ChannelMode", _channelSpread);
                    Graphics.Blit(extraA, auxA, _material, 21); // Pass 21: EncodeChannelSpace

                    _material.SetFloat("_SpreadX", _xVariations);
                    _material.SetFloat("_SpreadY", _yVariations);
                    Graphics.Blit(auxA, auxB, _material, 27); // Pass 27: SpreadPass

                    _material.SetTexture("_BaseTex", extraA);
                    _material.SetInt("_ChannelMode", _channelSpread);
                    Graphics.Blit(auxB, destination, _material, 26); // Pass 26: DecodeAndMergeChannel
                }
                else
                {
                    Graphics.Blit(curr, destination);
                }
            }
            finally
            {
                ReleaseTemp(rtA);
                ReleaseTemp(rtB);
                ReleaseTemp(auxA);
                ReleaseTemp(auxB);
                ReleaseTemp(extraA);
                ReleaseTemp(qA);
                ReleaseTemp(qB);
                ReleaseTemp(jfaA);
                ReleaseTemp(jfaB);
            }
        }

        public void ResetParameters()
        {
            foreach (FilterParameter param in _parameters)
            {
                param.Reset();
            }
        }

        public void Dispose()
        {
            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
            }
        }

        private RenderTexture RunMorpho(
            RenderTexture input,
            RenderTexture pingA,
            RenderTexture pingB,
            RenderTexture scratch,
            bool isDilate)
        {
            if (_size < 2)
            {
                Graphics.Blit(input, pingA);
                return pingA;
            }

            _material.SetFloat("_MorphoIsDilate", isDilate ? 1f : 0f);

            if (!_octagonalShape)
            {
                _material.SetInt("_MorphoSize", _size);
                Graphics.Blit(input, scratch, _material, 22); // Pass 22: MorphoSquareH
                Graphics.Blit(scratch, pingA, _material, 23); // Pass 23: MorphoSquareV
                return pingA;
            }

            // G'MIC erode_oct / dilate_oct decomposition into r diamond + q square steps.
            int ss = _size + 1 - (_size & 1);
            float sqrt2 = 1.41421356f;
            int r = Mathf.RoundToInt((ss - 1) * sqrt2 / (1f + sqrt2) * 0.5f);
            int q = Mathf.RoundToInt((ss - 1) / (1f + sqrt2) * 0.5f);

            RenderTexture cur = input;
            RenderTexture next = pingA;

            for (int step = 0; step < r; step++)
            {
                Graphics.Blit(cur, next, _material, 24); // Pass 24: MorphoDiamond3x3
                cur = next;
                next = (cur == pingA) ? pingB : pingA;
            }

            if (q > 0)
            {
                _material.SetInt("_MorphoSize", 2 * q + 1);
                Graphics.Blit(cur, scratch, _material, 22);
                Graphics.Blit(scratch, next, _material, 23);
                cur = next;
            }
            else if (cur == input)
            {
                Graphics.Blit(input, pingA);
                cur = pingA;
            }

            return cur;
        }

        private static RenderTexture GetTempRt(int width, int height, RenderTextureFormat format, FilterMode filterMode)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            rt.filterMode = filterMode;
            rt.wrapMode = TextureWrapMode.Clamp;
            return rt;
        }

        private static void ReleaseTemp(RenderTexture rt)
        {
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}