// The camcorder, in the order a real one makes a picture (CamcorderPipeline.cs runs the passes):
//   0 Integrate  the lens image (URP: glare, distortion, colour fringes, vignette, depth of field) is exposed onto a
//                recording raster (640 x 480 for 480 lines): exposure, the shutter's motion blur (1/60 s, from the camera's
//                own motion and the depth), the pixel footprint and optical softness. Linear light.
//   1 Meter      an 80 x 60 average of the exposed raster, read back by the CPU for the auto iris, gain and white balance.
//   2 Smear      the CCD's vertical smear: the mean of each column, added to the whole column.
//   3 Head       the camera head: smear, sensor noise (shot + read, grown by the AGC gain), fixed pattern and hot pixels,
//                black clip, white balance, saturation, the Rec.709 transfer curve, knee and white clip, highlight and
//                low-light colour suppression, temporal noise reduction. Out: Y'CbCr.
//   4 Encode     the recording and playback: timebase jitter, flagging, head switching, tracking, the noclip glitch;
//                luma bandwidth and aperture correction (detail), chroma bandwidth (analog colour-under or DV 4:1:1),
//                chroma delay and comb, DV compression, tape noise, chroma phase noise, dropouts, cross-colour, setup.
//   5 Display    the recording scaled to the screen (line doubling or interlace on request), the viewfinder overlay,
//                optional CRT scanlines.
// No shader keywords: one variant per pass. All parameters are material properties set per frame.
Shader "Hidden/Backrooms/CamcorderPipeline"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    float4 _CamRaster;      // x, y: raster size, z, w: 1 / size
    float4 _CamFrame;       // x: frame index, y: exposure multiplier, z: motion blur amount (1 = the shutter), w: softness (raster px)
    float4 _CamBlur;        // x: distance (m) under which things move with the camera (no blur), y: max smear (fraction of the frame)
    float4x4 _CamInvVPNow, _CamVPOpen;
    float4 _CamPosition;    // xyz: camera position
    TEXTURE2D_X_FLOAT(_CamDepth);

    TEXTURE2D(_CamRasterTex);
    TEXTURE2D(_CamSmearTex);
    TEXTURE2D(_CamHeadTex);
    TEXTURE2D(_CamHeadPrev);
    TEXTURE2D(_CamRecTex);

    float4 _CamSensor;      // x: AGC gain (linear), y: read noise, z: shot noise, w: noise multiplier
    float4 _CamNoise2;      // x: colour (CFA) noise share 0..1, y: fixed pattern, z: hot pixels, w: exposure time x 60
    float4 _CamSmearK;      // x: smear strength, y: purple fringing
    float4 _CamWB;          // rgb: white balance gains, w: saturation
    float4 _CamTone;        // x: transfer exponent (0.45), y: knee point, z: knee slope, w: white clip
    float4 _CamLowLight;    // x: low-light colour cut 0..1, y: temporal noise reduction 0..1, z: highlight chroma suppression start (0 = 0.8)

    float4 _CamFmt;         // x: analog (1) / DV (0), y: NTSC comb (0/1), z: DV 4:1:1 (0/1), w: DV compression
    float4 _CamLumaW;       // luma filter weights: x centre, y +-1, z +-2
    float4 _CamChroma;      // x: chroma sigma (raster px), y: trailing, z: delay (raster px), w: phase noise (radians)
    float4 _CamDetail;      // x: horizontal detail, y: coring, z: vertical detail, w: limiter
    float4 _CamTape;        // x: luma tape noise, y: chroma tape noise, z: dropouts per line per field, w: cross-colour
    float4 _CamTBC;         // x: line jitter (px), y: top flagging (px), z: head-switch lines, w: head-switch skew (px)
    float4 _CamTrack;       // x: tracking noise bar, y: bar position (0 top .. 1 bottom), z: setup (pedestal), w: bottom line
    float4 _CamGlitch;      // x: noclip glitch 0..1, y: roll (fraction of the frame), z: random per frame, w: time (s)

    float4 _CamDisplay;     // x: line doubling 0..1, y: interlace (0 off, 1 bob), z: field parity, w: CRT scanlines
    float4 _CamOSD;         // x: OSD on (0/1), y: OSD curvature, z, w: unused
    float4 _CamViewRect;    // xy: the camera viewport's corner, zw: its size (screen fractions), for the overlay texture
    float4 _CamDrops[16];   // water on the lens (LensDrops.cs): xy centre (uv), z radius (share of the height), w strength
    float4 _CamWash;        // a sheet of water over the lens (LensDrops.Wash): x how much is left, y seconds since
    float4 _CamLensFog;     // condensation on the lens (LensFog.cs): x amount, y how far it has cleared (from the edges in)
    TEXTURE2D(_CamOSDTex);

    // ---------------- Noise ----------------

    uint3 Pcg3d(uint3 v)
    {
        v = v * 1664525u + 1013904223u;
        v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
        v ^= v >> 16u;
        v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
        return v;
    }
    float3 Rand3(int2 p, uint frame, uint salt) { return float3(Pcg3d(uint3(asuint(p.x), asuint(p.y), frame * 2654435761u + salt))) * (1.0 / 4294967296.0); }
    // Approximately Gaussian (sum of three uniforms), zero mean, unit variance.
    float Gauss(int2 p, uint frame, uint salt) { float3 r = Rand3(p, frame, salt); return (r.x + r.y + r.z - 1.5) * 2.0; }
    float Hash(int2 p, uint frame, uint salt) { return Rand3(p, frame, salt).x; }
    // Noise along a line, smooth over 'cells' pixels: the horizontally streaky noise of tape.
    float Streak(float x, int ln, uint field, uint salt)
    {
        float i = floor(x), f = x - i;
        return lerp(Gauss(int2((int)i, ln), field, salt), Gauss(int2((int)i + 1, ln), field, salt), f * f * (3 - 2 * f));
    }

    float3 SampleSource(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, clamp(uv, 0.0005, 0.9995), 0).rgb; }
    float3 Raster(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CamRasterTex, sampler_LinearClamp, uv, 0).rgb; }
    float3 Head(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CamHeadTex, sampler_LinearClamp, uv, 0).xyz; }
    float HeadY(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CamHeadTex, sampler_LinearClamp, uv, 0).x; }
    float2 HeadC(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CamHeadTex, sampler_LinearClamp, uv, 0).yz; }

    float3 FromYCC(float3 v) { return float3(v.x + 1.403 * v.z, v.x - 0.344 * v.y - 0.714 * v.z, v.x + 1.770 * v.y); }

    // ---------------- 0 Integrate ----------------

    static const float2 kRotatedGrid[4] = { float2(0.125, 0.375), float2(0.375, -0.125), float2(-0.125, -0.375), float2(-0.375, 0.125) };

    // Water on the lens: each drop a small lens of its own (the scene through it small and upside down), blurred, with a
    // dark rim and a bright edge toward the light.
    float2 LensDrops(float2 uv, inout float blur, inout float shade)
    {
        float aspect = _CamRaster.x / max(_CamRaster.y, 1.0);
        [loop] for (int i = 0; i < 16; i++)
        {
            float4 d = _CamDrops[i];
            if (d.w <= 0) continue;
            float2 q = (uv - d.xy) * float2(aspect, 1.0) / d.z;
            q.y *= q.y < 0 ? 1.25 : 0.9;                        // heavier at the bottom: it is running down
            float r = length(q);
            if (r >= 1.0) continue;
            float k = d.w * smoothstep(1.0, 0.8, r);
            uv = lerp(uv, d.xy - (uv - d.xy) * 0.55, k);
            blur = max(blur, k);
            float rim = smoothstep(0.7, 0.97, r) * (1.0 - smoothstep(0.97, 1.0, r));
            shade *= lerp(1.0, 0.72 + 0.6 * saturate(q.y * 1.5) * (1.0 - smoothstep(0.75, 0.95, r)), rim * d.w) * lerp(1.0, 0.92, k);
        }
        return uv;
    }

    float4 FragIntegrate(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float lensBlur = 0, lensShade = 1;
        // A wash of water running down the lens: streams of it bending the picture and smearing it downward, the top
        // clearing first.
        if (_CamWash.x > 0.001)
        {
            float aspect = _CamRaster.x / max(_CamRaster.y, 1.0);
            float t = _CamWash.y;
            float clear = saturate(t * 0.7);
            float k = _CamWash.x * smoothstep(1.0 - clear - 0.2, 1.0 - clear + 0.1, 1.0 - uv.y);
            float2 q = float2(uv.x * aspect * 9.0, uv.y * 2.5 + t * 1.7);
            float2 cellId = floor(q);
            float sx = frac(sin(dot(cellId.xx, float2(12.9898, 78.233))) * 43758.5453);
            float stream = smoothstep(0.35, 0.0, abs(frac(q.x) - 0.5 - (sx - 0.5) * 0.3)) * (0.6 + 0.4 * sin(q.y * 6.2832 + sx * 6.0));
            uv += float2((stream - 0.3) * 0.012, -0.02 * stream - 0.006) * k;
            lensBlur = max(lensBlur, k * (0.8 + stream));
            lensShade *= 1.0 - 0.08 * k;
        }
        uv = LensDrops(uv, lensBlur, lensShade);
        // A misted lens: patchy, clearing from the edges toward the middle; it spreads and greys the light.
        float fog = 0;
        if (_CamLensFog.x > 0)
        {
            float aspect = _CamRaster.x / max(_CamRaster.y, 1.0);
            float r = length((input.texcoord - 0.5) * float2(aspect, 1.0)) / (0.5 * sqrt(aspect * aspect + 1.0));
            float edge = 1.0 - _CamLensFog.y * 1.15;
            float2 cell = floor(input.texcoord * float2(aspect, 1.0) * 9.0);
            float patchy = 0.75 + 0.25 * Rand3(int2(cell), 7u, 91u).x;
            fog = _CamLensFog.x * (1.0 - smoothstep(edge - 0.25, edge, r)) * patchy;
            lensBlur = max(lensBlur, fog * 2.2);
        }
        uint frame = (uint)_CamFrame.x;
        // Where this point of the scene was when the shutter opened: the streak it leaves behind (trailing the motion).
        float2 delta = 0;
        if (_CamFrame.z > 0)
        {
            float d = SAMPLE_TEXTURE2D_X_LOD(_CamDepth, sampler_PointClamp, uv, 0).r;
            float3 ws = ComputeWorldSpacePosition(uv, d, _CamInvVPNow);
            float2 uvOpen = ComputeNormalizedDeviceCoordinates(ws, _CamVPOpen);
            // The arm and what it holds move with the camera: no smear from turning on them.
            float attached = smoothstep(_CamBlur.x, _CamBlur.x + 0.4, distance(ws, _CamPosition.xyz));
            delta = clamp((uv - uvOpen) * _CamFrame.z * attached, -_CamBlur.y, _CamBlur.y);
        }
        float lengthPx = length(delta * _CamRaster.xy);
        int n = (int)clamp(max(ceil(lengthPx), fog > 0.02 ? 24.0 : 4.0), 4, 32);
        // Along the streak: stratified with a random start per pixel (fine grain, hidden by the sensor noise, never a pattern).
        float j = Hash((int2)input.positionCS.xy, frame, 31u);
        // The pixel's footprint: a fixed pattern (a still picture does not shimmer). Along a streak longer than the footprint
        // the streak itself covers it, so the pattern then only spreads across the motion (irregular offsets along the
        // streak would let fine texture leak through the blur).
        float2 dir = lengthPx > 1e-3 ? normalize(delta * _CamRaster.xy) : float2(1, 0), across = float2(-dir.y, dir.x);
        float alongKeep = saturate(1.0 - lengthPx / (1.0 + _CamFrame.w));
        float3 acc = 0;
        [loop] for (int k = 0; k < n; k++)
        {
            float s = (k + j) / n;
            float2 o = k < 4 ? kRotatedGrid[k] : frac(0.5 + k * float2(0.7548777, 0.5698403)) - 0.5;
            if (fog > 0.02) o = (frac(j + k * float2(0.7548777, 0.5698403)) - 0.5) * 1.4;     // a soft disc, not copies
            float2 sub = (dir * (dot(o, dir) * alongKeep) + across * dot(o, across)) * (1.0 + _CamFrame.w + lensBlur * 7.0) * _CamRaster.zw;
            acc += SampleSource(uv + delta * s + sub);
        }
        float3 col = acc / n * _CamFrame.y * lensShade;
        if (fog > 0)
        {
            float l = dot(col, float3(0.3, 0.59, 0.11));
            col = lerp(col, (col * 0.45 + l * 0.55) * 0.9 + l * 0.18 + 0.012 * _CamFrame.y, fog * 0.75);
        }
        return float4(col, 1);
    }

    // ---------------- 1 Meter ----------------

    float4 FragMeter(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord, q = 0.25 / float2(80, 60);
        float3 c = Raster(uv + float2(-q.x, -q.y)) + Raster(uv + float2(q.x, -q.y)) + Raster(uv + float2(-q.x, q.y)) + Raster(uv + float2(q.x, q.y));
        return float4(c * 0.25, 1);
    }

    // ---------------- 2 Smear ----------------

    float4 FragSmear(Varyings input) : SV_Target
    {
        // Charge that leaks into a column's vertical register while the frame shifts out: the whole column's light, a little.
        float u = input.texcoord.x;
        float3 s = 0;
        [loop] for (float y = 1.0; y < _CamRaster.y; y += 2.0)
            s += Raster(float2(u, y * _CamRaster.w)).rgb;
        s *= 2.0 * _CamRaster.w;
        return float4(lerp(s, dot(s, 1.0 / 3.0).xxx, 0.5), 1);
    }

    // ---------------- 3 Head ----------------

    float Oetf(float x, float gamma)
    {
        // Rec.709-style: a straight toe joined to the power curve (exponent from the contrast setting).
        float toe = (1.099 * pow(0.018, gamma) - 0.099) / 0.018;
        return x < 0.018 ? toe * x : 1.099 * pow(x, gamma) - 0.099;
    }

    // Knee: a smooth bend at the knee point into a shallow slope (bright light compressed), then a hard white clip.
    float3 Knee(float3 v, float kp, float ks, float wc)
    {
        const float w = 0.05;
        float3 x = v - kp;
        float3 soft = kp + x + (ks - 1) * (x + w) * (x + w) / (4 * w);
        float3 k = v <= kp - w ? v : (v >= kp + w ? kp + x * ks : soft);
        return min(k, wc);
    }

    float4 FragHead(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        int2 p = (int2)(uv * _CamRaster.xy);
        uint frame = (uint)_CamFrame.x;
        float3 L = Raster(uv);
        L += SAMPLE_TEXTURE2D_LOD(_CamSmearTex, sampler_LinearClamp, float2(uv.x, 0.5), 0).rgb * _CamSmearK.x;
        // Purple fringing: blue-violet hugging the edges of blown-out lights.
        if (_CamSmearK.y > 0)
        {
            float2 o = _CamRaster.zw * 1.5;
            float4 nb = float4(Luminance(Raster(uv + float2(o.x, 0))), Luminance(Raster(uv - float2(o.x, 0))),
                               Luminance(Raster(uv + float2(0, o.y))), Luminance(Raster(uv - float2(0, o.y))));
            float overMax = saturate(max(max(nb.x, nb.y), max(nb.z, nb.w)) / 1.5 - 1), overHere = saturate(Luminance(L) / 1.5 - 1);
            L += max(0, overMax - overHere) * float3(0.35, 0.05, 0.6) * _CamSmearK.y;
        }
        // Sensor noise: shot noise grows with the light, read noise does not; both grow with the AGC gain. Partly common to
        // the three colours (luma), partly not (the colour filter's chroma noise).
        float g = _CamSensor.x;
        float3 signal = max(L, 0) / g;
        float3 sigma = g * sqrt(_CamSensor.y * _CamSensor.y + _CamSensor.z * signal) * _CamSensor.w;
        float common = Gauss(p, frame, 11u);
        float3 own = float3(Gauss(p, frame, 13u), Gauss(p, frame, 17u), Gauss(p, frame, 19u));
        float b = _CamNoise2.x;
        L += (common * sqrt(1 - b * b) + own * b) * sigma * float3(1.15, 1.0, 1.3);
        // Fixed pattern (the same every frame) and hot pixels (at high gain and long exposure).
        float3 fp = Rand3(p, 0x9E37u, 23u);
        L += (fp.x - 0.5) * 0.0015 * g * _CamNoise2.y;
        L += step(fp.y, 6e-5) * fp.z * 0.02 * g * g * _CamNoise2.w * _CamNoise2.z * float3(1.0, 0.85, 0.9);
        // Black clip, white balance, saturation.
        L = max(L, 0) * _CamWB.rgb;
        float yl = dot(L, float3(0.2126, 0.7152, 0.0722));
        L = max(yl + (L - yl) * _CamWB.w, 0);
        float3 V = float3(Oetf(L.r, _CamTone.x), Oetf(L.g, _CamTone.x), Oetf(L.b, _CamTone.x));
        V = Knee(V, _CamTone.y, _CamTone.z, _CamTone.w);
        float Y = dot(V, float3(0.299, 0.587, 0.114));
        float2 C = float2((V.b - Y) * 0.564, (V.r - Y) * 0.713);
        // Highlight chroma suppression: colour fades out from a level below the clip to none at it, so clipped lights are
        // white, not tinted, and bright walls go pale.
        float chromaStart = _CamLowLight.z > 0 ? _CamLowLight.z : 0.8;
        C *= saturate((_CamTone.w - Y) / max(_CamTone.w - chromaStart, 1e-3));
        C *= lerp(1, smoothstep(0.03, 0.15, Y), _CamLowLight.x);             // gained-up shadows lose their colour
        float3 o = float3(Y, C);
        // Temporal noise reduction (DV camcorders in low light): still areas average with the last frame; moving ones
        // leave the faint ghost trails of real footage.
        if (_CamLowLight.y > 0.001)
        {
            float3 prev = SAMPLE_TEXTURE2D_LOD(_CamHeadPrev, sampler_PointClamp, uv, 0).xyz;
            float a = _CamLowLight.y * saturate(1 - abs(prev.x - Y) / 0.06);
            o = lerp(o, prev, a);
        }
        return float4(o, 1);
    }

    // ---------------- 4 Encode ----------------

    float4 FragEncode(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float W = _CamRaster.x, H = _CamRaster.y;
        uint frame = (uint)_CamFrame.x;
        uint field = frame;
        float glitch = _CamGlitch.x;
        float blank = 0;
        // The noclip glitch: the picture rolls (the blanking bar at the seam), bands tear sideways.
        if (glitch > 0.001)
        {
            float roll = _CamGlitch.y;
            uv.y = frac(uv.y + roll);
            float seam = min(uv.y, 1.0 - uv.y);
            blank = roll > 0.001 ? smoothstep(0.035 * glitch + 0.003, 0.0, seam) : 0;
        }
        float lineTop = (1.0 - uv.y) * H;                  // 0 at the top of the picture
        int ln = (int)lineTop;
        float x = uv.x * W;

        // Timebase: each line starts a little early or late (no time-base corrector), the top lines bend after the head
        // switch, the last lines before it skew sideways.
        float dx = _CamTBC.x * (Streak(lineTop * 0.07 + field * 0.37, 0, 0u, 701u) * 0.5 + 0.35 * (Hash(int2(ln, 0), field, 703u) - 0.5));
        dx += _CamTBC.y * exp(-lineTop / 10.0) * (Streak(field * 0.21, 1, 0u, 709u) * 0.5);
        float hsT = saturate((lineTop - (H - _CamTBC.z)) / max(_CamTBC.z, 1));
        dx += hsT * hsT * _CamTBC.w * (1 + 0.25 * (Hash(int2(7, 0), field, 711u) - 0.5));
        // Tracking: a noise bar where the heads read between two tracks.
        float bar = _CamTrack.x > 0.001 ? exp(-pow((lineTop / H - _CamTrack.y) * H / 6.0, 2)) * _CamTrack.x : 0;
        dx += bar * 3.0 * Gauss(int2(ln, 1), field, 713u);
        if (glitch > 0.001)
        {
            float bands = lerp(9.0, 46.0, frac(_CamGlitch.z * 7.13));
            float bandId = floor(uv.y * bands);
            float tear = step(1.15 - glitch * 0.85, abs(Gauss(int2((int)bandId, 9), field, 523u)));
            dx += Gauss(int2((int)bandId, 3), field, 517u) * 0.11 * W * glitch * tear * 0.5;
            dx += Gauss(int2(ln, 5), field, 529u) * 3.0 * glitch;
        }
        float2 px = float2(1.0 / W, 1.0 / H);
        float2 at = float2(uv.x + dx / W, uv.y);

        // Luma: the format's bandwidth (5 taps) and the camera's aperture correction (horizontal band-pass, vertical from
        // the 1H delay lines), cored, level-dependent, limited.
        float y_2 = HeadY(at - float2(2 * px.x, 0)), y_1 = HeadY(at - float2(px.x, 0)), y0 = HeadY(at);
        float y1 = HeadY(at + float2(px.x, 0)), y2 = HeadY(at + float2(2 * px.x, 0));
        float Y = _CamLumaW.x * y0 + _CamLumaW.y * (y_1 + y1) + _CamLumaW.z * (y_2 + y2);
        float yUp = HeadY(at + float2(0, px.y)), yDn = HeadY(at - float2(0, px.y));
        float dh = y0 - 0.5 * (y_2 + y2), dv = y0 - 0.5 * (yUp + yDn);
        dh = sign(dh) * max(abs(dh) - _CamDetail.y, 0);
        dv = sign(dv) * max(abs(dv) - _CamDetail.y, 0);
        Y += clamp((dh * _CamDetail.x + dv * _CamDetail.z) * smoothstep(0.04, 0.18, y0), -_CamDetail.w, _CamDetail.w);

        // Chroma: colour-under (analog: narrow, trailing to the right, a little late, averaged with the line above by the
        // comb) or DV 4:1:1 (one colour sample per four pixels).
        float2 C = 0;
        if (_CamFmt.z > 0.5)
        {
            float cell = (x + dx - 2.0) / 4.0, c0 = floor(cell), f = cell - c0;
            float2 a = 0.5 * (HeadC(float2((c0 * 4 + 1.0 + 0.5) / W, at.y)) + HeadC(float2((c0 * 4 + 3.0 + 0.5) / W, at.y)));
            float2 b = 0.5 * (HeadC(float2((c0 * 4 + 5.0 + 0.5) / W, at.y)) + HeadC(float2((c0 * 4 + 7.0 + 0.5) / W, at.y)));
            C = lerp(a, b, f);
        }
        else
        {
            float ws = 0;
            [unroll] for (int k = -4; k <= 4; k++)
            {
                float w = exp(-0.125 * k * k) * (k > 0 ? exp(-k * _CamChroma.y) : 1.0);
                C += HeadC(float2((x + dx - _CamChroma.z + k * _CamChroma.x * 0.5 + 0.5) / W, at.y)) * w;
                ws += w;
            }
            C /= ws;
            if (_CamFmt.y > 0.5) C = 0.5 * (C + HeadC(float2((x + dx - _CamChroma.z + 0.5) / W, at.y + px.y)));
        }

        // DV compression: 8 x 8 blocks; low-contrast detail inside a busy or noisy block flattens, sharp edges get mosquito noise.
        if (_CamFmt.w > 0.001)
        {
            float2 bp = floor(float2(x, lineTop) / 8.0) * 8.0;
            float4 q = float4(HeadY(float2((bp.x + 2) / W, 1 - (bp.y + 2) / H)), HeadY(float2((bp.x + 6) / W, 1 - (bp.y + 2) / H)),
                              HeadY(float2((bp.x + 2) / W, 1 - (bp.y + 6) / H)), HeadY(float2((bp.x + 6) / W, 1 - (bp.y + 6) / H)));
            float m = dot(q, 0.25), act = max(max(q.x, q.y), max(q.z, q.w)) - min(min(q.x, q.y), min(q.z, q.w));
            float qs = _CamFmt.w * (0.012 + 0.25 * act);
            float dev = Y - m;
            Y = m + sign(dev) * max(abs(dev) - qs, 0);
            Y += qs * 0.8 * (Hash(int2((int)x, ln), frame, 731u) - 0.5) * saturate(act * 6);
            C = lerp(C, C * 0.7 + 0.3 * HeadC(float2((bp.x + 4) / W, 1 - (bp.y + 4) / H)), saturate(_CamFmt.w));
        }

        // Analog playback: colour phase and level wander line by line, streaky tape noise, rainbow cross-colour on fine detail.
        if (_CamFmt.x > 0.5)
        {
            // The colour's errors come off the tape line by line; the playback comb then averages each line with the one above.
            bool comb = _CamFmt.y > 0.5;
            int up = ln - 1;
            float ph = Gauss(int2(ln, 2), field, 741u), amp = Gauss(int2(ln, 3), field, 743u);
            float2 cn = float2(Streak(x / 8.0, ln, field, 757u), Streak(x / 8.0, ln, field, 759u));
            if (comb)
            {
                ph = 0.5 * (ph + Gauss(int2(up, 2), field, 741u)); amp = 0.5 * (amp + Gauss(int2(up, 3), field, 743u));
                cn = 0.5 * (cn + float2(Streak(x / 8.0, up, field, 757u), Streak(x / 8.0, up, field, 759u)));
            }
            ph *= _CamChroma.w; amp = 1 + 0.5 * _CamChroma.w * amp;
            C = float2(C.x * cos(ph) - C.y * sin(ph), C.x * sin(ph) + C.y * cos(ph)) * amp;
            Y += _CamTape.x * (0.7 * Gauss(int2((int)x, ln), field, 751u) + 0.3 * Streak(x / 3.0, ln, field, 753u));
            C += _CamTape.y * cn;
            float ph2 = 6.2831853 * 0.294 * x + 3.14159265 * (ln + field);
            C += _CamTape.w * dh * float2(cos(ph2), sin(ph2));
        }

        // Dropouts: short white dashes along a line for one field (more in the tracking bar), a soft tail after each.
        float rate = _CamTape.z * (1 + 40 * bar);
        if (rate > 0)
        {
            float r = Hash(int2(ln, 11), field, 761u);
            if (r < rate)
            {
                float x0 = Hash(int2(ln, 12), field, 763u) * W;
                float len = W * (0.01 + 0.10 * pow(Hash(int2(ln, 13), field, 767u), 3));
                float u = (x - x0) / len;
                if (u > 0 && u < 1.6)
                {
                    float k = u < 1 ? 1 : exp(-(u - 1) * 6);
                    Y = lerp(Y, 0.92, k); C *= 1 - k;
                }
            }
        }

        // Head switching: the last lines' colour is garbled, a line of noise where the heads swap.
        if (hsT > 0)
        {
            C *= 1 - 0.7 * hsT;
            C += 0.08 * hsT * float2(Hash(int2(ln, 21), field, 771u) - 0.5, Hash(int2(ln, 22), field, 773u) - 0.5);
            if (abs(lineTop - (H - _CamTBC.z)) < 0.75) Y = lerp(Y, Hash(int2((int)x, ln), field, 777u), 0.6);
        }
        // The tracking bar itself: noisy, a little bright.
        if (bar > 0.01) Y += bar * (0.03 + 0.12 * max(Gauss(int2((int)(x / 2), ln), field, 779u), 0));

        // Bottom line (the episode's capture): lilac over the right part of the last few lines.
        if (_CamTrack.w > 0.001)
        {
            float v = saturate(1.0 - (H - lineTop) / 4.0);
            v *= v;
            if (v > 0)
            {
                float split = 0.46 + 0.004 * sin(_CamGlitch.w * 1.7);
                float right = smoothstep(split - 0.004, split + 0.004, uv.x);
                float spot = exp(-pow((uv.x - split - 0.006) / 0.009, 2.0));
                Y += (0.02 * lerp(0.2, 1.0, right) + 0.06 * spot) * v * _CamTrack.w;
                C += (float2(0.11, 0.035) * lerp(0.2, 1.0, right) + float2(-0.02, 0.06) * spot) * v * _CamTrack.w;
            }
        }

        // The glitch, after: dropouts everywhere, colour killed (the burst lost), the blanking bar.
        if (glitch > 0.001)
        {
            float seg = floor(uv.x * 4.0 + Hash(int2(ln, 31), field, 781u) * 4.0);
            float on = step(1.0 - 0.06 * glitch * glitch, Hash(int2(ln, (int)seg), field, 783u));
            float white = step(0.35, Hash(int2((int)seg, ln), field, 787u));
            Y = lerp(Y, white > 0.5 ? 0.92 : 0.02, on * 0.85);
            C *= glitch > 0.5 ? 0 : 1 - glitch;
            Y += Gauss(int2((int)(x / 2), ln), field, 789u) * 0.1 * glitch;
            Y *= 1 - blank; C *= 1 - blank;
        }

        // Setup: video black sits a little above zero (7.5 IRE on analog NTSC).
        Y = _CamTrack.z + Y * (1 - _CamTrack.z);
        return float4(Y, C, 1);
    }

    // ---------------- 5 Display ----------------

    float3 RecAt(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_CamRecTex, sampler_LinearClamp, uv, 0).xyz; }

    // Catmull-Rom from 5 bilinear taps (a scaler's bicubic), for the progressive picture.
    float3 RecCubic(float2 uv)
    {
        float2 size = _CamRaster.xy, inv = _CamRaster.zw;
        float2 pos = uv * size, c = floor(pos - 0.5) + 0.5, f = pos - c;
        float2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f)), w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
        float2 w2 = f * (0.5 + f * (2.0 - 1.5 * f)), w3 = f * f * (-0.5 + 0.5 * f);
        float2 w12 = w1 + w2, o12 = w2 / w12;
        float2 t0 = (c - 1) * inv, t3 = (c + 2) * inv, t12 = (c + o12) * inv;
        float3 r = RecAt(float2(t12.x, t0.y)) * (w12.x * w0.y) + RecAt(float2(t0.x, t12.y)) * (w0.x * w12.y) +
                   RecAt(t12) * (w12.x * w12.y) + RecAt(float2(t3.x, t12.y)) * (w3.x * w12.y) + RecAt(float2(t12.x, t3.y)) * (w12.x * w3.y);
        float wsum = w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
        return r / wsum;
    }

    float4 FragDisplay(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float H = _CamRaster.y;
        float3 ycc;
        if (_CamDisplay.y > 0.5)
        {
            // Interlace shown as bob: only this field's lines, each doubled (thin horizontal edges twitter).
            float yr = (1.0 - uv.y) * H - 0.5, par = _CamDisplay.z;
            float yf = (yr - par) * 0.5, y0 = floor(yf), t = yf - y0;
            t = lerp(t, step(0.5, t), _CamDisplay.x);
            float l0 = 2 * y0 + par, l1 = l0 + 2;
            ycc = lerp(RecAt(float2(uv.x, 1 - (l0 + 0.5) / H)), RecAt(float2(uv.x, 1 - (l1 + 0.5) / H)), t);
        }
        else if (_CamDisplay.x > 0.001)
        {
            // Line doubling: each recorded line shown as a band.
            float g = uv.y * H - 0.5, i = floor(g), f = g - i;
            f = lerp(f, step(0.5, f), _CamDisplay.x);
            ycc = RecAt(float2(uv.x, (i + f + 0.5) / H));
        }
        else ycc = RecCubic(uv);
        float3 V = FromYCC(ycc);

        // Viewfinder overlay (not part of the recording): flat or slightly bent like the eyepiece.
        if (_CamOSD.x > 0.5)
        {
            float2 suv = _CamViewRect.xy + uv * _CamViewRect.zw;
            float aspect = _ScreenParams.x / _ScreenParams.y;
            float2 c = (suv - 0.5) * float2(aspect, 1);
            float r2 = dot(c, c) / (0.25 * (aspect * aspect + 1));
            float k = _CamOSD.y * 0.09;
            float2 ouv = 0.5 + (suv - 0.5) * (1 + k * r2) / (1 + k * 0.35);
            if (all(ouv > 0) && all(ouv < 1))
            {
                float4 o = SAMPLE_TEXTURE2D_LOD(_CamOSDTex, sampler_LinearClamp, ouv, 0);
                float3 osd = LinearToSRGB(o.rgb / max(o.a, 1e-3));
                V = lerp(V, osd, o.a);
            }
        }

        // CRT scanlines (a TV filmed or shown, not the recording), box-filtered over the pixel so they never shimmer.
        if (_CamDisplay.w > 0.001)
        {
            float period = _ScreenParams.y * _CamViewRect.w / H;
            float aa = period > 1e-3 ? (abs(1.0 / period) < 1e-4 ? 1.0 : sin(PI / period) / (PI / period)) : 0;
            float profile = 0.5 + 0.5 * cos(2 * PI * uv.y * H) * aa;
            V *= 1 - _CamDisplay.w * profile;
        }
        return float4(SRGBToLinear(saturate(V)), 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off
        Pass { Name "Integrate" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragIntegrate
            ENDHLSL }
        Pass { Name "Meter" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMeter
            ENDHLSL }
        Pass { Name "Smear" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSmear
            ENDHLSL }
        Pass { Name "Head" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHead
            ENDHLSL }
        Pass { Name "Encode" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragEncode
            ENDHLSL }
        Pass { Name "Display" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDisplay
            ENDHLSL }
    }
}
