# Overheating glow

This page covers the natural overheating visual, its rendering limits and reproducible checks.
It does not change thermal simulation, damage, the warning band or thermal-camera rendering.

## Design and acceptance criteria

The replacement uses a smooth radial core and halo in one depth-tested, additive billboard per
visible exposed face. The texture must have zero RGB and alpha on every border, radial symmetry,
a monotone falloff and reproducible mipmaps. A face must fade continuously to zero at grazing
angles and at the 2,000 m draw boundary. Size and surface offset must scale with grid size.
The existing Planckian colour and linear last-100-K warning ramp remain authoritative.

The renderer must submit no more than 4,000 quads per frame, including many-grid scenes, and hold
no more than 32 nearby heat lights. Disabling HeatGlow or unloading must release every light;
dedicated servers must do no rendering. Per-face calculation must allocate zero managed bytes
once warmed. These are deterministic work and allocation bounds, not a claim about GPU time.

Offline acceptance requires the full solution to compile, focused presentation and existing heat
cue tests to pass, asset regeneration to match byte-for-byte, and documentation checks to run.
Visual acceptance additionally requires a live game check: small and large armour, slopes and
functional blocks; isolated and adjacent hot blocks; cold/half/full ramp; frontal/grazing views;
bright and dark scenes; cockpit, occlusion, cooldown, switch-off and world unload. No offline
preview can certify game bloom, whitelist acceptance or the appearance of transparent geometry.

## References and evidence

* [WeaponCore-StarCore](https://github.com/StarCoreSE/WeaponCore-StarCore), inspected at
  commit `98ce92d32037fd361a676494e5e3cc1bdd1ff6ab`: WeaponController writes the named Heating
  emissive on configured parts. This supports temperature-driven emissives where a model provides
  them, but cannot give arbitrary armour a new emissive material. No source or assets are copied.
* Workshop item `571920453`, supplied local installation: SEDrag.MyLightMethod creates a heat
  light and CloseLight removes it. The replacement keeps pooled lighting and avoids the reference's
  per-light shadow/reflector cost. No source or assets are copied.
* Installed SE1 Content/Shaders/Transparent/Billboards.hlsl multiplies texture RGBA by billboard
  RGBA; Transparent/OIT/Globals.hlsli passes premultiplied colour through for non-OIT rendering.
  The glow asset therefore carries falloff in RGB as well as alpha: white RGB with soft alpha alone
  is insufficient evidence that an additive quad has no rectangular edge.

## Limits

This is a soft surface cue, not a radiometrically calibrated blackbody renderer. Brightness is a
warning keyed to each block's critical rating, as specified in document-of-intent.md; the radial
profile and view fade are artistic approximations. Exposed bounding faces do not conform to slopes,
curved meshes, holes, deformation or animated subparts. Soft borders and grazing fade suppress the
box silhouette but do not create mesh emissivity. Adjacent patches can overlap. A shadowless light
can leak through walls and a single grid light cannot represent disconnected hotspots accurately.
Hard work limits can omit geometry or lights; grid iteration order is not a nearest-first sort.

## Validation record

Implementation and results are pending. No live game appearance or frame-time claim is made.

## Change log

| Date | Change |
| --- | --- |
| 2026-09-20 | Record reference findings, scope, limits and acceptance criteria before implementation. |
