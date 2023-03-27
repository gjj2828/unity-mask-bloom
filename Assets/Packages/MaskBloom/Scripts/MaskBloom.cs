using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

namespace mattatz.MaskBloom {

    [RequireComponent (typeof(Camera) )]
    public class MaskBloom : MonoBehaviour {

        public enum BloomType {
            Screen,
            Add
        };

        [SerializeField] BloomType type = BloomType.Screen;
        [SerializeField] Shader shader; // assign mattatz/MaskBloom shader.
        Material material;

        [SerializeField] int
            blurIterations = 3,
            blurDownSample = 2;

        [SerializeField][Range(0.0f, 5.0f)] float
            bloomIntensity = 1.0f;

        [SerializeField][Range(0, 255)] int
            stencil = 1;

        [SerializeField] bool debug;

        [SerializeField]
        private RawImage m_RawImage;
        [SerializeField]
        private bool m_Render2Tex;

        private Camera m_Camera;
        private RenderTexture m_RenderTexture;
        private int m_LastScreenWidth;
        private int m_LastScreenHeight;

        private bool IsRender2Tex => m_Render2Tex && m_RawImage != null;
        private bool IsCurRender2Tex => m_RenderTexture != null;

        void Start() {
            material = new Material(shader);

            m_Camera = GetComponent<Camera>();

            if (IsRender2Tex)
            {
                EnableRender2Tex();
            }
        }

        private void OnDestroy()
        {
            if(IsCurRender2Tex)
            {
                DisableRender2Tex();
            }
        }

        void Update() {
            blurIterations = Mathf.Max(1, blurIterations);
            blurDownSample = Mathf.Max(0, blurDownSample);
            bloomIntensity = Mathf.Max(0f, bloomIntensity);

            if (IsCurRender2Tex != IsRender2Tex)
            {
                if(IsRender2Tex)
                {
                    EnableRender2Tex();
                }
                else
                {
                    DisableRender2Tex();
                }
            }
            else if(IsCurRender2Tex)
            {
                int screenWidth = Screen.width;
                int screenHeight = Screen.height;

                if(m_LastScreenWidth != screenWidth || m_LastScreenHeight != screenHeight)
                {
                    var lastRT = m_RenderTexture;
                    m_RenderTexture = CreateRT(screenWidth, screenHeight);
                    m_Camera.targetTexture = m_RenderTexture;
                    if(m_RawImage != null)
                    {
                        m_RawImage.texture = m_RenderTexture;
                    }
                    lastRT.Release();

                    m_LastScreenWidth = screenWidth;
                    m_LastScreenHeight = screenHeight;
                }
            }
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst) {

            // Gaussian Blur
            var downSampled = DownSample(src, blurDownSample);
            Blur(downSampled, blurIterations);

            if (debug)
            {
                Graphics.Blit(downSampled, dst);
                RenderTexture.ReleaseTemporary(downSampled);
                return;
            }

            // Bloom
            material.SetFloat("_Intensity", bloomIntensity);
            material.SetTexture("_BlurTex", downSampled);
            material.SetInt("_StencilRef", stencil);

            var tmp = RenderTexture.GetTemporary(src.width, src.height, 0, src.graphicsFormat, src.antiAliasing);

            switch (type)
            {
                case BloomType.Screen:
                    Graphics.Blit(src, tmp, material, 4);
                    break;

                case BloomType.Add:
                    Graphics.Blit(src, tmp, material, 5);
                    break;

                default:
                    Graphics.Blit(src, tmp, material, 4);
                    break;
            }

            var oldRt = RenderTexture.active;

            Graphics.SetRenderTarget(tmp.colorBuffer, src.depthBuffer);

            material.SetTexture("_MainTex", src);
            material.SetPass(6);

            GL.Clear(false, false, Color.clear);

            GL.PushMatrix();
            GL.LoadOrtho();

            // draw a quad over whole screen
            GL.Begin(GL.QUADS);
            GL.TexCoord2(0.0f, 0.0f);
            GL.Vertex3(0.0f, 0.0f, 0.0f);
            GL.TexCoord2(1.0f, 0.0f);
            GL.Vertex3(1.0f, 0.0f, 0.0f);
            GL.TexCoord2(1.0f, 1.0f);
            GL.Vertex3(1.0f, 1.0f, 0.0f);
            GL.TexCoord2(0.0f, 1.0f);
            GL.Vertex3(0.0f, 1.0f, 0.0f);
            GL.End();

            GL.PopMatrix();

            RenderTexture.active = oldRt;

            Graphics.Blit(tmp, dst);

            RenderTexture.ReleaseTemporary(tmp);
            RenderTexture.ReleaseTemporary(downSampled);
        }

        public void Blur(RenderTexture src, int nIterations) {
            var tmp0 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            var tmp1 = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            var iters = Mathf.Clamp(nIterations, 0, 10);

            Graphics.Blit(src, tmp0);
            for (var i = 0; i < iters; i++) {
                for (var pass = 2; pass < 4; pass++) {
                    tmp1.DiscardContents();
                    tmp0.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(tmp0, tmp1, material, pass);
                    var tmpSwap = tmp0;
                    tmp0 = tmp1;
                    tmp1 = tmpSwap;
                }
            }
            Graphics.Blit(tmp0, src);

            RenderTexture.ReleaseTemporary(tmp0);
            RenderTexture.ReleaseTemporary(tmp1);
        }

        public RenderTexture DownSample(RenderTexture src, int lod) {
            var dst = RenderTexture.GetTemporary(src.width, src.height, 0, src.format);
            src.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, dst);

            for (var i = 0; i < lod; i++) {
                var tmp = RenderTexture.GetTemporary(dst.width >> 1, dst.height >> 1, 0, dst.format);
                dst.filterMode = FilterMode.Bilinear;
                Graphics.Blit(dst, tmp, material, 0);
                RenderTexture.ReleaseTemporary(dst);
                dst = tmp;
            }

            var mask = RenderTexture.GetTemporary(dst.width, dst.height, 0, dst.format);
            mask.filterMode = FilterMode.Bilinear;
            Graphics.Blit(dst, mask, material, 1); // masking
            RenderTexture.ReleaseTemporary(dst);
            return mask;
        }

        private void EnableRender2Tex()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;

            m_RenderTexture = CreateRT(screenWidth, screenHeight);
            m_Camera.targetTexture = m_RenderTexture;

            if(m_RawImage != null)
            {
                m_RawImage.texture = m_RenderTexture;

                var uiCam = m_RawImage.canvas.worldCamera;
                if (uiCam != null)
                {
                    uiCam.enabled = true;
                }
            }

            m_LastScreenWidth = screenWidth;
            m_LastScreenHeight = screenHeight;
        }

        private void DisableRender2Tex()
        {
            if(m_RawImage != null)
            {
                var uiCam = m_RawImage.canvas.worldCamera;
                if(uiCam != null)
                {
                    uiCam.enabled = false;
                }
                m_RawImage.texture = null;
            }

            m_Camera.targetTexture = null;

            if(m_RenderTexture != null)
            {
                m_RenderTexture.Release();
                m_RenderTexture = null;
            }
        }

        private RenderTexture CreateRT(int width, int height)
        {
            return new RenderTexture(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt);
        }
    }

}


