# ShadowVSM
 Adds VSM shadows to Unity projects, replacing the default shadows

 *** Based on https://github.com/gkjohnson/unity-custom-shadow-experiments/ ***


This is a (hopefully) generally useful package that adds VSM shadows to Unity projects,
replacing the default shadows.  Notable improvements:

* It gives reasonable-quality shadows on Oculus Quest (and probably mobile targets
  in general).  On Quest the built-in shadows are always blocky.

* It is based on VSM (Variance Shadow Maps), which gives nicer smoothing.  At the moment
  there is only one simple block-blur done on the shadow map, but the idea is that it is
  possible to do many block-blur passes or more advanced image filtering on it, even
  though this is not possible with the traditional kind of shadow maps.

Limitations: it works only in the traditional pipeline (not HDRP/LDRP), and was only
tested in the forward rendering mode.

Demo: see the scene in the Assets/Demo/ directory.  You can see the kind of shadows we
get and how they are getting more and more fuzzy and less detailed when we look far from
the main camera.

How to use:

(1) Likely, you want to completely disable the built-in shadows in Unity.
    (Project Settings -> Quality -> Shadows -> Shadows -> Disable Shadows)

(2) Drop the prefab "ShadowVSM Prefab" into all your scenes, or arrange to have
    it instantiated there, or put it only once and make it DontDestroyOnLoad.

(3) All your materials should use the provided shaders if they are supposed to
    receive shadows (see the "ShadowVSM/Shaders" folder, or the "ShadowVSM" section in
    the list of shader names).


By default all objects with "RenderType" = "Opaque" should cast shadows.  See the
"Limit shadow casters" options in the ShadowVSM prefab.

"Shadow computation" can be changed if you have a situation where the shadowmaps don't
need to be recomputed every frame.  "Automatic Incremental Cascade" will recompute it
incrementally over N frames, where N is the number of cascades (see below).  Or,
"Manual from script" means it will only recompute when you call the methods
ShadowVSM.UpdateShadowsFull() or ShadowVSM.UpdateShadowsIncrementalCascade().

Note how even with a low resolution of 512x512, the shadowmap displays not too badly.
You can choose to increase this resolution to improve it further.

Shadow cascades are in powers of two.  They are concentric boxes centered (by default)
on the directional light of the scene, each one twice as big as the previous one.  Note
that traditionally we'd make boxes starting at the camera and looking forward only;
indeed it is wasteful to compute shadowmaps for parts of the world that are not
displayed.  I didn't fix this so far, because that is necessary in "Manual from script"
mode: computing shadows independently from the actual camera position and rotation and
reusing them even if the camera turns around and moves.  In that mode, the script also
needs to check if the camera has moved too far from the initial position, because then
it reaches low-precision cascades that are not meant to be looked at closely.  At that
point the script needs to force a recomputation.
