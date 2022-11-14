using UnityEngine;
using System.Collections;
using System.Collections.Generic;


[ExecuteInEditMode]
public class ShadowVSM : MonoBehaviour
{
    public enum ShadowComputation
    {
        ManualFromScript,
        AutomaticFull,
        AutomaticIncrementalCascade,
    }

    [Header("Shadow computation")]
    [Tooltip("ManualFromScript: shadows are only updated when you call UpdateShadowsXxx(). " +
             "AutomaticFull: shadows are updated every frame. " +
             "AutomaticIncrementalCascade: shadows take numCascades frames to update.")]
    public ShadowComputation _shadowComputation = ShadowComputation.AutomaticFull;
    [Tooltip("Set this to false to disable the automatic shadow camera positioning. " +
             "See SetShadowCameraPosition().")]
    public bool _shadowCameraFollowsMainCamera = true;

    [Header("Initialization")]
    public Shader _depthShader;
    public Shader blurShader;
    Material _blur_material;

    [Header("Shadow Settings")]
    public int _resolution = 512;
    public int numCascades = 6;

    public float firstCascadeLevelSize = 8.0f;
    public float depthOfShadowRange = 1000.0f;
    public FilterMode _filterMode = FilterMode.Bilinear;
    public bool useDitheringForTransparent = false;
    public enum HighPrecisionMode { Full, Half, HalfOnAndroidOnly }
    public HighPrecisionMode highPrecisionMode = HighPrecisionMode.HalfOnAndroidOnly;

    [Header("Limit shadow casters")]
    public LayerMask cullingMask = -1;
    [Tooltip("If you use the provided depthShader, this can be either RenderType to render only "+
             "RenderType='opaque' shaders, or it can be an empty string to render all objects " +
             "including transparent ones.")]
    public string depthReplacementShaderTag = "RenderType";


    /* Hack: if non-null, it is a list of Materials on which to apply the VSM_xxx properties.
     * If null (the default), we set them globally. */
    internal List<Material> vsm_materials;


    float internal_scale = 1f;


    /* RenderTextures:
     *   "_target" is the ShadowCam target.  Still needs to be filtered.  Size is 'res x res'.
     *   (where res = "_resolution").
     *
     * The other two RenderTextures are size 'res x (res*numCascades)'.  They each store two
     * at-least-16-bit float components: the depth and the square of the depth.  On some
     * platforms, the exact rendertexture format is not available; we fall back to either two
     * 32-bit floats, or if that fails too, we try full RGBA textures.
     *
     * The "backTarget" is read by the shaders to display the shadows.  If we are only
     * using non-incremental mode, then the "renderBackTarget" is the same texture object.
     * If we're using incremental mode, then "renderBackTarget" is a different texture.
     * It is the one in which we are currently rendering the new shadows, swapped when we're
     * done.
     */
    RenderTexture _backTarget, _target;
    RenderTexture _renderBackTarget;
    Camera _shadowCam;


    void OnEnable()
    {
        if (Application.isPlaying)
            switch (_shadowComputation)
            {
                case ShadowComputation.AutomaticFull:
                    Camera.onPreRender += AutomaticFull;
                    break;

                case ShadowComputation.AutomaticIncrementalCascade:
                    Camera.onPreRender += AutomaticIncrementalCascade;
                    break;
            }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
            switch (_shadowComputation)
            {
                case ShadowComputation.AutomaticFull:
                    Camera.onPreRender -= AutomaticFull;
                    break;

                case ShadowComputation.AutomaticIncrementalCascade:
                    Camera.onPreRender -= AutomaticIncrementalCascade;
                    break;
            }
        DestroyInternals();
    }

#if UNITY_EDITOR
    private void Update()
    {
        if (!Application.isPlaying)
            UpdateShadowsFull();
    }
#endif

    int most_recent_frame_number = -1;

    void AutomaticFull(Camera cam)
    {
        if (Time.frameCount != most_recent_frame_number)
        {
            most_recent_frame_number = Time.frameCount;
            UpdateShadowsFull();
        }
    }

    void AutomaticIncrementalCascade(Camera cam)
    {
        if (Time.frameCount != most_recent_frame_number)
        {
            most_recent_frame_number = Time.frameCount;

            if (_auto_incr_cascade == null)
                _auto_incr_cascade = UpdateShadowsIncrementalCascade();
            if (!_auto_incr_cascade.MoveNext())
                _auto_incr_cascade = null;
        }
    }

    IEnumerator _auto_incr_cascade;

    struct ComputeData
    {
        internal int numCascades, resolution;
        internal float firstCascadeLevelSize, depthOfShadowRange;
    }

    void InitComputeData(out ComputeData cdata)
    {
        /* ComputeData stores parameters that we want to remain constant over several frames
         * in UpdateShadowsIncrementalCascade(), even if they are public fields that may be
         * modified at a random point. */
        cdata.numCascades = numCascades;
        cdata.resolution = _resolution;
        cdata.firstCascadeLevelSize = firstCascadeLevelSize;
        cdata.depthOfShadowRange = depthOfShadowRange;
    }

    public IEnumerator UpdateShadowsIncrementalCascade()
    {
        /* Update one cascade between each yield.  It has no visible effect, until it has
         * been resumed "numCascades - 1" times, i.e. until it has computed the last cascade;
         * at this point it really updates the shadows and finishes. */
        if (!InitializeUpdateSteps(true))
            yield break;

        InitComputeData(out ComputeData cdata);

        for (int i = cdata.numCascades - 1; i >= 0; i--)
        {
            ComputeCascade(i, cdata);
            if (i > 0)
                yield return null;
        }

        FinalizeUpdateSteps(cdata);
    }

    public void UpdateShadowsFull()
    {
        _auto_incr_cascade = null;
        if (!InitializeUpdateSteps(false))
            return;

        InitComputeData(out ComputeData cdata);
        for (int i = cdata.numCascades - 1; i >= 0; i--)
            ComputeCascade(i, cdata);
        FinalizeUpdateSteps(cdata);
    }

    bool InitializeUpdateSteps(bool incremental)
    {
        if (!UpdateRenderTexture(incremental))
            return false;

        SetUpShadowCam();
        _shadowCam.targetTexture = _target;

        if (useDitheringForTransparent) Shader.EnableKeyword("VSM_DRAW_TRANSPARENT_SHADOWS");
        else Shader.DisableKeyword("VSM_DRAW_TRANSPARENT_SHADOWS");

        _blur_material.SetVector("BlurPixelSize", new Vector2(1f / _resolution, 1f / _resolution));
        return true;
    }

    void ComputeCascade(int lvl, ComputeData cdata)
    {
        _shadowCam.orthographicSize = cdata.firstCascadeLevelSize * internal_scale * Mathf.Pow(2, lvl);
        _shadowCam.nearClipPlane = -cdata.depthOfShadowRange * internal_scale;
        _shadowCam.farClipPlane = cdata.depthOfShadowRange * internal_scale;
        try
        {
            _shadowCam.RenderWithShader(_depthShader, depthReplacementShaderTag);
        }
        catch (System.NullReferenceException)
        {
            /* VRSKETCH-CS: from UnityEngine.UI.InputField.GenerateCaret.
             * That's almost surely a Unity bug.  We can't do anything here, we'll just
             * display wrong shadows. */

            /* VRSKETCH-CS shows the same error occurring on versions of VR SKetch with this
             * hack, though, so I guess it's not a propagated exception?  I can't do anything
             * but ignore on Sentry then :-( */
        }

        var rt = RenderTexture.GetTemporary(_resolution, _resolution, 0, _target_format_mode);
        rt.wrapMode = TextureWrapMode.Clamp;

        _blur_material.EnableKeyword("BLUR_Y");
        CustomBlit(_target, rt, _blur_material, 0, 1);
        _blur_material.DisableKeyword("BLUR_Y");

        float y1 = lvl / (float)cdata.numCascades;
        float y2 = (lvl + 1) / (float)cdata.numCascades;

        _blur_material.EnableKeyword("BLUR_LINEAR_AND_SQUARE_PART");
        CustomBlit(rt, _renderBackTarget, _blur_material, y1, y2);
        _blur_material.DisableKeyword("BLUR_LINEAR_AND_SQUARE_PART");

        RenderTexture.ReleaseTemporary(rt);
    }

    void FinalizeUpdateSteps(ComputeData cdata)
    {
        CustomBlit(null, _renderBackTarget, _blur_material, 1f - 1f / _renderBackTarget.height, 1f);
        Swap(ref _backTarget, ref _renderBackTarget);   /* might be identical, if !incremental */
        UpdateShaderValues(cdata);
    }

    static void CustomBlit(Texture source, RenderTexture target, Material mat, float y1, float y2)
    {
        var original = RenderTexture.active;
        RenderTexture.active = target;

        // Set the '_MainTex' variable to the texture given by 'source'
        mat.SetTexture("_MainTex", source);
        GL.PushMatrix();
        GL.LoadOrtho();
        // activate the first shader pass (in this case we know it is the only pass)
        mat.SetPass(0);

        GL.Begin(GL.QUADS);
        GL.TexCoord2(0f, 0f); GL.Vertex3(0f, y1, 0f);
        GL.TexCoord2(0f, 1f); GL.Vertex3(0f, y2, 0f);
        GL.TexCoord2(1f, 1f); GL.Vertex3(1f, y2, 0f);
        GL.TexCoord2(1f, 0f); GL.Vertex3(1f, y1, 0f);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = original;
    }

    void DestroyTargets()
    {
        if (_target)
        {
            DestroyImmediate((Texture)_target);
            _target = null;
        }
        if (_backTarget)
        {
            DestroyImmediate((Texture)_backTarget);
            _backTarget = null;
        }
        if (_renderBackTarget)
        {
            DestroyImmediate((Texture)_renderBackTarget);
            _renderBackTarget = null;
        }
    }

    // Disable the shadows
    void DestroyInternals()
    {
        if (_shadowCam)
        {
            DestroyImmediate((GameObject)_shadowCam.gameObject);
            _shadowCam = null;
        }
        DestroyTargets();
        //ForAllKeywords(s => Shader.DisableKeyword(ToKeyword(s)));
    }

    private void OnDestroy()
    {
        DestroyInternals();
    }

    Camera FetchShadowCamera()
    {
        if (_shadowCam == null)
        {
            // Create the shadow rendering camera
            GameObject go = new GameObject("shadow cam (not saved)");
            //go.hideFlags = HideFlags.HideAndDontSave;
            go.hideFlags = HideFlags.DontSave;

            _shadowCam = go.AddComponent<Camera>();
            _shadowCam.orthographic = true;
            _shadowCam.enabled = false;
            /* the shadow camera renders to three components:
             *    r: depth, scaled in [-64, 64]
             *    g: r * r
             *    b: 1
             * The blue component is used to special-case pixels where nothing was drawn at all,
             * which have b == 0 and thus don't count in the blurring algorithm. */
            _shadowCam.backgroundColor = new Color(0, 0, 0, 0);
            _shadowCam.clearFlags = CameraClearFlags.SolidColor;
            _shadowCam.aspect = 1;

            if (_blur_material == null)
                _blur_material = new Material(blurShader);
            _blur_material.SetColor("_Color", _shadowCam.backgroundColor);
        }
        return _shadowCam;
    }

    Light _main_light_cache;

    Light GetMainLight()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            _main_light_cache = null;
#endif
        if (_main_light_cache == null)
        {
            foreach (var light1 in FindObjectsOfType<Light>())
                if (light1.type == LightType.Directional)
                    if (_main_light_cache == null || light1.intensity > _main_light_cache.intensity)
                        _main_light_cache = light1;
        }
        return _main_light_cache;
    }

    void SetUpShadowCam()
    {
        var cam = FetchShadowCamera();

        /* Set up the clip planes so that we store depth values in the range [-0.5, 0.5],
         * with values near zero being near us even if depthOfShadowRange is very large.
         * This maximizes the precision in the RHalf textures near us. */
        cam.cullingMask = cullingMask;

        if (_shadowCameraFollowsMainCamera)
        {
            Camera maincam = Camera.main;
            if (maincam == null)
            {
                Debug.LogError("ShadowVSM: Camera.main is null");
            }
            else
            {
                Light sun = RenderSettings.sun;
                if (sun == null)
                    sun = GetMainLight();

                if (sun == null)
                {
                    Debug.LogError("ShadowVSM: no directional Light found in the scene");
                }
                else
                {
                    cam.transform.SetPositionAndRotation(maincam.transform.position,
                                                         sun.transform.rotation);
                }
            }
        }
    }

    public void SetShadowCameraPosition(Vector3 position, Quaternion rotation, float scale = 1f)
    {
        /* For _shadowCameraFollowsMainCamera == false.  Move the shadow camera to the given
         * global position, rotation, and optionally scale.  Use this in ManualFromScript mode
         * if the shadows only involve semi-static objects, but these semi-static objects can
         * move and you want the shadows on them to move with them.
         */
        var cam = FetchShadowCamera();
        cam.transform.SetPositionAndRotation(position, rotation);
        internal_scale = scale;

        InitComputeData(out ComputeData cdata);
        UpdateShadowCamTransformShaderValues(cdata);
    }

    delegate void PropGlobalSetter(int id);
    delegate void PropMaterialSetter(Material mat, int id);
    void SetShaderProperty(string name, PropGlobalSetter glob_setter, PropMaterialSetter mat_setter)
    {
        var id = Shader.PropertyToID(name);
        if (vsm_materials == null)
            glob_setter(id);
        else
            foreach (var mat in vsm_materials)
                mat_setter(mat, id);
    }

    void UpdateShadowCamTransformShaderValues(ComputeData cdata)
    {
        Vector3 size;
        size.y = cdata.firstCascadeLevelSize * internal_scale * 2;
        size.x = _shadowCam.aspect * size.y;
        size.z = cdata.depthOfShadowRange * internal_scale * 2;

        size.x = 1f / size.x;
        size.y = 1f / size.y;
        size.z = 128f / size.z;

        var mat = _shadowCam.transform.worldToLocalMatrix;
        var light_matrix = Matrix4x4.Scale(size) * mat;
        var light_matrix_normal = Matrix4x4.Scale(Vector3.one * 1.2f / cdata.resolution) * mat;

        SetShaderProperty("VSM_LightMatrix",
            id => Shader.SetGlobalMatrix(id, light_matrix),
            (mat1, id) => mat1.SetMatrix(id, light_matrix));
        SetShaderProperty("VSM_LightMatrixNormal",
            id => Shader.SetGlobalMatrix(id, light_matrix_normal),
            (mat1, id) => mat1.SetMatrix(id, light_matrix_normal));

#if UNITY_EDITOR
        ShowRenderTexturesForDebugging();
#endif
    }

    void UpdateShaderValues(ComputeData cdata)
    {
        // Set the qualities of the textures
        SetShaderProperty("VSM_ShadowTex",
            id => Shader.SetGlobalTexture(id, _backTarget),
            (mat1, id) => mat1.SetTexture(id, _backTarget));
        SetShaderProperty("VSM_InvNumCascades",
            id => Shader.SetGlobalFloat(id, 1f / cdata.numCascades),
            (mat1, id) => mat1.SetFloat(id, 1f / cdata.numCascades));

        UpdateShadowCamTransformShaderValues(cdata);
    }

#if UNITY_EDITOR
    class RenderTextureDebugging : MonoBehaviour
    {
        public RenderTexture target, backTarget;
        public RenderTexture incrementalRendering;
    }
    void ShowRenderTexturesForDebugging()
    {
        var rtd = _shadowCam.GetComponent<RenderTextureDebugging>();
        if (rtd == null)
            rtd = _shadowCam.gameObject.AddComponent<RenderTextureDebugging>();
        rtd.target = _target;
        rtd.backTarget = _backTarget;
        rtd.incrementalRendering = _backTarget == _renderBackTarget ? null : _renderBackTarget;
    }
#endif

    // Refresh the render target if the scale has changed
    bool UpdateRenderTexture(bool incremental)
    {
        if (_target != null && _target.width != _resolution)
            DestroyTargets();
        if (_backTarget != null && (_backTarget.filterMode != _filterMode ||
                                    _backTarget.height != _resolution * numCascades))
            DestroyTargets();

        if (_target == null)
        {
            if (numCascades <= 0 || _resolution <= 0)
                return false;
            _target = CreateTarget();
        }
        if (_backTarget == null)
        {
            _backTarget = CreateBackTarget();
        }
        if (_renderBackTarget == null || (incremental && _renderBackTarget == _backTarget))
        {
            if (incremental)
            {
                _renderBackTarget = CreateBackTarget();
            }
            else
            {
                _renderBackTarget = _backTarget;
            }
        }
        return true;
    }

    void UpdateShadowCameraPos(Transform trackTransform)
    {
        Camera cam = _shadowCam;
        cam.transform.SetPositionAndRotation(trackTransform.position, trackTransform.rotation);
        cam.transform.localScale = trackTransform.lossyScale;
    }

#if UNITY_ANDROID
    bool FullPrecision => highPrecisionMode == HighPrecisionMode.Full;
#else
    bool FullPrecision => highPrecisionMode != HighPrecisionMode.Half;
#endif
    RenderTextureFormat _target_format_mode;

    RenderTextureFormat GetTargetMode()
    {
        if (!FullPrecision && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            return RenderTextureFormat.ARGBHalf;
        if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            return RenderTextureFormat.ARGBFloat;
        throw new System.Exception("ARGBFloat mode is not supported!");
    }

    RenderTextureFormat GetBackTargetMode()
    {
        if (!FullPrecision && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf))
            return RenderTextureFormat.RGHalf;
        if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat))
            return RenderTextureFormat.RGFloat;
        if (!FullPrecision && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            return RenderTextureFormat.ARGBHalf;
        if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            return RenderTextureFormat.ARGBFloat;
        throw new System.Exception("RGFloat and ARGBFloat modes are both not supported!");
    }

    // Creates a rendertarget
    RenderTexture CreateTarget()
    {
        _target_format_mode = GetTargetMode();
        var desc = new RenderTextureDescriptor(_resolution, _resolution, _target_format_mode, 24);
        //desc.enableRandomWrite = true;
        RenderTexture tg = new RenderTexture(desc);
        tg.wrapMode = TextureWrapMode.Clamp;
        tg.antiAliasing = 4;
        tg.Create();
        return tg;
    }

    RenderTexture CreateBackTarget()
    {
        var tg = new RenderTexture(_resolution, _resolution * numCascades, 0, GetBackTargetMode());
        tg.filterMode = _filterMode;
        tg.wrapMode = TextureWrapMode.Clamp;
        tg.Create();
        return tg;
    }

    // Swap Elements A and B
    static void Swap<T>(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }
}
