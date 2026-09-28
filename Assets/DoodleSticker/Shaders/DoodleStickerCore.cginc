#ifndef DOODLE_STICKER_CORE_INCLUDED
#define DOODLE_STICKER_CORE_INCLUDED

#ifndef DOODLE_STICKER_SAMPLE_ART
    #error "Define DOODLE_STICKER_SAMPLE_ART(atlasUV, atlasUVDdx, atlasUVDdy) before including DoodleStickerCore.cginc"
#endif

#define DOODLE_STICKER_MIN_PEEL_DISTANCE 1e-4
#define DOODLE_STICKER_HIGHLIGHT_ANGLE 0.55
#define DOODLE_STICKER_HIGHLIGHT_EXPONENT 24.0
#define DOODLE_STICKER_SHEEN_ANGLE 0.35
#define DOODLE_STICKER_GLUE_BLOTCH_SCALE 0.08

float _StickerOpacity;

float _DoodleWobbleTiling;
float _DoodleWobbleStrength;
float _DoodleWobbleFramesPerSecond;
float _DoodleStickerUnscaledTime;

float _PageCurlRadius;
float _PageCurlShading;
float _PageCurlHighlightStrength;

fixed4 _PageCurlShadowColor;
float _PageCurlShadowSoftness;
float4 _PageCurlShadowOffset;

fixed4 _PageCurlBackfaceColor;
float _PageCurlArtBleed;
sampler2D _PageCurlBackfaceTexture;
float4 _PageCurlBackfaceTexture_ST;

fixed4 _PageCurlEdgeColor;
float _PageCurlEdgeWidth;
float _PageCurlSheenStrength;
float _PageCurlSheenWidth;
float _PageCurlFoldShading;
fixed4 _PageCurlGlueMarkColor;
float _PaperGrainStrength;
float _PaperGrainScale;

struct DoodleStickerSurface
{
    float2 stickerUV;
    float4 atlasUVRect;
    float2 curlSpaceScale;
    float2 grabPoint;
    float2 dragPoint;
    fixed4 tint;
};

struct DoodleStickerSamplingContext
{
    float4 atlasUVRect;
    float2 atlasUVDdx;
    float2 atlasUVDdy;
    float2 stickerUVDdx;
    float2 stickerUVDdy;
    float2 stickerEdgeSoftness;
    fixed3 tint;
};

struct DoodleStickerFold
{
    float2 origin;
    float2 axis;
    float radius;
};

struct DoodleStickerSheetLayers
{
    float distanceFromFold;
    float curlAngle;
    float2 lowerSourceUV;
    float2 upperSourceUV;
};

float GetDoodleWobbleTime()
{
    #ifdef _DOODLE_STICKER_UNSCALED_TIME
    return _DoodleStickerUnscaledTime;
    #else
    return _Time.y;
    #endif
}

float2 EvaluateDoodleWobbleOffset(float2 stickerUV)
{
    float wobbleFrameIndex = floor(GetDoodleWobbleTime() * _DoodleWobbleFramesPerSecond);
    float2 wobbleWave = float2(
        sin((stickerUV.x * _DoodleWobbleTiling + wobbleFrameIndex) * 4.0),
        cos((stickerUV.y * _DoodleWobbleTiling + wobbleFrameIndex) * 4.0));
    return wobbleWave * _DoodleWobbleStrength;
}

fixed4 SampleStickerArt(DoodleStickerSamplingContext context, float2 sourceStickerUV)
{
    float2 wobbledStickerUV = sourceStickerUV + EvaluateDoodleWobbleOffset(sourceStickerUV);
    float2 atlasUV = context.atlasUVRect.xy + wobbledStickerUV * context.atlasUVRect.zw;
    fixed4 artColor = DOODLE_STICKER_SAMPLE_ART(atlasUV, context.atlasUVDdx, context.atlasUVDdy);

    float2 distanceToRectEdge = min(wobbledStickerUV, 1.0 - wobbledStickerUV);
    float2 rectEdgeCoverage = saturate(distanceToRectEdge / context.stickerEdgeSoftness + 0.5);
    artColor.a *= rectEdgeCoverage.x * rectEdgeCoverage.y;
    artColor.rgb *= context.tint;
    return artColor;
}

float HashPaperCell(float2 cell)
{
    return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
}

float EvaluateValueNoise(float2 position)
{
    float2 cell = floor(position);
    float2 blend = frac(position);
    blend = blend * blend * (3.0 - 2.0 * blend);
    float bottom = lerp(HashPaperCell(cell), HashPaperCell(cell + float2(1.0, 0.0)), blend.x);
    float top = lerp(HashPaperCell(cell + float2(0.0, 1.0)), HashPaperCell(cell + float2(1.0, 1.0)), blend.x);
    return lerp(bottom, top, blend.y);
}

float EvaluateCutEdge(DoodleStickerSamplingContext context, float2 sourceStickerUV, float centerAlpha)
{
    float2 tapOffset = context.stickerEdgeSoftness * _PageCurlEdgeWidth;
    float minimumNeighbourAlpha = min(
        min(SampleStickerArt(context, sourceStickerUV + float2(tapOffset.x, tapOffset.y)).a,
            SampleStickerArt(context, sourceStickerUV + float2(-tapOffset.x, tapOffset.y)).a),
        min(SampleStickerArt(context, sourceStickerUV + float2(tapOffset.x, -tapOffset.y)).a,
            SampleStickerArt(context, sourceStickerUV + float2(-tapOffset.x, -tapOffset.y)).a));
    return saturate(1.0 - minimumNeighbourAlpha) * centerAlpha;
}

fixed3 EvaluateBackfaceColor(DoodleStickerSamplingContext context, float2 sourceStickerUV, fixed3 artColor)
{
    fixed3 backfaceColor = _PageCurlBackfaceColor.rgb;

    #ifdef _DOODLE_STICKER_PAPER_DETAIL
    float paperGrain = EvaluateValueNoise(sourceStickerUV * _PaperGrainScale) * 0.65
        + EvaluateValueNoise(sourceStickerUV * _PaperGrainScale * 2.7) * 0.35;
    backfaceColor *= 1.0 + (paperGrain - 0.5) * 2.0 * _PaperGrainStrength;
    #endif

    #ifdef _DOODLE_STICKER_BACKFACE_TEXTURE
    float2 backfaceUV = TRANSFORM_TEX(sourceStickerUV, _PageCurlBackfaceTexture);
    float2 backfaceUVDdx = context.stickerUVDdx * _PageCurlBackfaceTexture_ST.xy;
    float2 backfaceUVDdy = context.stickerUVDdy * _PageCurlBackfaceTexture_ST.xy;
    backfaceColor *= tex2Dgrad(_PageCurlBackfaceTexture, backfaceUV, backfaceUVDdx, backfaceUVDdy).rgb;
    #endif

    return lerp(backfaceColor, backfaceColor * artColor, _PageCurlArtBleed);
}

DoodleStickerFold BuildDoodleStickerFold(float2 grabCurlPoint, float2 dragCurlPoint)
{
    float2 peelVector = grabCurlPoint - dragCurlPoint;
    float peelDistance = max(length(peelVector), DOODLE_STICKER_MIN_PEEL_DISTANCE);

    DoodleStickerFold fold;
    fold.axis = peelVector / peelDistance;
    fold.radius = max(min(_PageCurlRadius, peelDistance / UNITY_PI), DOODLE_STICKER_MIN_PEEL_DISTANCE);
    fold.origin = grabCurlPoint - fold.axis * (peelDistance + UNITY_PI * fold.radius) * 0.5;
    return fold;
}

DoodleStickerSheetLayers EvaluateSheetLayers(DoodleStickerFold fold, float2 curlPoint, float2 curlSpaceScale)
{
    DoodleStickerSheetLayers layers;
    layers.distanceFromFold = dot(curlPoint - fold.origin, fold.axis);
    layers.curlAngle = asin(saturate(layers.distanceFromFold / fold.radius));

    float2 pointOnFoldLine = curlPoint - layers.distanceFromFold * fold.axis;
    float stuckDistance = min(layers.distanceFromFold, 0.0);
    float curlArcLength = layers.curlAngle * fold.radius;

    layers.lowerSourceUV = (pointOnFoldLine + fold.axis * (stuckDistance + curlArcLength)) / curlSpaceScale;
    layers.upperSourceUV = (pointOnFoldLine + fold.axis * (UNITY_PI * fold.radius - stuckDistance - curlArcLength)) / curlSpaceScale;
    return layers;
}

float EvaluateShadowCasterAt(DoodleStickerSamplingContext context, DoodleStickerFold fold, float2 casterCurlPoint, float2 curlSpaceScale, float shadowEdgeWidth, bool includeRollingPart)
{
    DoodleStickerSheetLayers casterLayers = EvaluateSheetLayers(fold, casterCurlPoint, curlSpaceScale);
    float casterSheetCoverage = saturate((fold.radius - casterLayers.distanceFromFold) / shadowEdgeWidth + 0.5);
    float casterAlpha = SampleStickerArt(context, casterLayers.upperSourceUV).a;

    if (includeRollingPart)
    {
        float casterLiftedMask = saturate(casterLayers.distanceFromFold / shadowEdgeWidth + 0.5);
        casterAlpha = max(casterAlpha, SampleStickerArt(context, casterLayers.lowerSourceUV).a * casterLiftedMask);
    }
    return casterAlpha * casterSheetCoverage;
}

float EvaluateDropShadowCaster(DoodleStickerSamplingContext context, DoodleStickerFold fold, float2 curlPoint, float2 curlSpaceScale, float edgeWidth, float receiverDistanceFromFold)
{
    float casterHeightFraction = saturate(abs(receiverDistanceFromFold) / (2.0 * fold.radius));
    float2 shadowCentre = curlPoint - _PageCurlShadowOffset.xy * casterHeightFraction;
    float shadowEdgeWidth = max(edgeWidth, _PageCurlShadowSoftness);
    float tapRadius = _PageCurlShadowSoftness * 0.7071;

    float blurredCaster = EvaluateShadowCasterAt(context, fold, shadowCentre, curlSpaceScale, shadowEdgeWidth, true) * 0.36;
    blurredCaster += EvaluateShadowCasterAt(context, fold, shadowCentre + float2(tapRadius, tapRadius), curlSpaceScale, shadowEdgeWidth, false) * 0.16;
    blurredCaster += EvaluateShadowCasterAt(context, fold, shadowCentre + float2(-tapRadius, tapRadius), curlSpaceScale, shadowEdgeWidth, false) * 0.16;
    blurredCaster += EvaluateShadowCasterAt(context, fold, shadowCentre + float2(tapRadius, -tapRadius), curlSpaceScale, shadowEdgeWidth, false) * 0.16;
    blurredCaster += EvaluateShadowCasterAt(context, fold, shadowCentre + float2(-tapRadius, -tapRadius), curlSpaceScale, shadowEdgeWidth, false) * 0.16;
    return blurredCaster;
}

float4 EvaluatePeeledSticker(DoodleStickerSamplingContext context, DoodleStickerSurface surface, float edgeWidth)
{
    float2 curlPoint = surface.stickerUV * surface.curlSpaceScale;
    DoodleStickerFold fold = BuildDoodleStickerFold(surface.grabPoint * surface.curlSpaceScale, surface.dragPoint * surface.curlSpaceScale);
    DoodleStickerSheetLayers layers = EvaluateSheetLayers(fold, curlPoint, surface.curlSpaceScale);

    fixed4 lowerArt = SampleStickerArt(context, layers.lowerSourceUV);
    fixed4 upperArt = SampleStickerArt(context, layers.upperSourceUV);

    float sheetCoverage = saturate((fold.radius - layers.distanceFromFold) / edgeWidth + 0.5);
    float liftedMask = saturate(layers.distanceFromFold / edgeWidth + 0.5);
    float curlShade = lerp(1.0, cos(layers.curlAngle), _PageCurlShading);
    float curlHighlight = _PageCurlHighlightStrength * liftedMask
        * pow(saturate(cos(layers.curlAngle - DOODLE_STICKER_HIGHLIGHT_ANGLE)), DOODLE_STICKER_HIGHLIGHT_EXPONENT);

    float liftedLowerAlpha = lowerArt.a * sheetCoverage;
    float upperAlpha = upperArt.a * sheetCoverage;

    float shadowSoftness = max(_PageCurlShadowSoftness, 1e-4);
    float contactShadow = max(lowerArt.a, upperArt.a)
        * exp(-max(layers.distanceFromFold - fold.radius, 0.0) / shadowSoftness);

    #ifdef _DOODLE_STICKER_DROP_SHADOW
    float dropShadow = EvaluateDropShadowCaster(context, fold, curlPoint, surface.curlSpaceScale, edgeWidth, layers.distanceFromFold);
    #else
    float dropShadow = 0.0;
    #endif

    float stuckShadowAlpha = saturate(dropShadow * _PageCurlShadowColor.a);
    float liftedShadowAlpha = saturate(max(contactShadow, dropShadow) * _PageCurlShadowColor.a);
    fixed3 backfaceColor = EvaluateBackfaceColor(context, layers.upperSourceUV, upperArt.rgb);

    float flapShade = 1.0;
    float sheen = 0.0;
    float cutEdge = 0.0;
    float4 glueMarkLayer = 0.0;

    #ifdef _DOODLE_STICKER_PAPER_DETAIL
    float foldedDistance = max(-layers.distanceFromFold, 0.0);
    flapShade = 1.0 - _PageCurlFoldShading * 0.25 * saturate(foldedDistance / (4.0 * fold.radius));

    float sheenExponent = lerp(40.0, 3.0, _PageCurlSheenWidth);
    float flapSheenReach = fold.radius * (1.0 + 6.0 * _PageCurlSheenWidth);
    float sheenPosition = layers.distanceFromFold < 0.0 ? -foldedDistance / flapSheenReach : layers.curlAngle;
    sheen = _PageCurlSheenStrength * pow(saturate(cos(sheenPosition - DOODLE_STICKER_SHEEN_ANGLE)), sheenExponent);

    cutEdge = EvaluateCutEdge(context, layers.upperSourceUV, upperArt.a) * _PageCurlEdgeColor.a;

    float originalArtAlpha = SampleStickerArt(context, surface.stickerUV).a;
    float glueBlotch = lerp(0.55, 1.0, EvaluateValueNoise(surface.stickerUV * _PaperGrainScale * DOODLE_STICKER_GLUE_BLOTCH_SCALE));
    float glueMarkAlpha = _PageCurlGlueMarkColor.a * originalArtAlpha * liftedMask * glueBlotch;
    glueMarkLayer = float4(_PageCurlGlueMarkColor.rgb * glueMarkAlpha, glueMarkAlpha);
    #endif

    fixed3 upperColor = saturate(backfaceColor * curlShade * flapShade + curlHighlight + sheen);
    upperColor = lerp(upperColor, _PageCurlEdgeColor.rgb * curlShade, cutEdge);

    float4 stuckRegionColor = float4(lowerArt.rgb * lowerArt.a, lowerArt.a) * (1.0 - stuckShadowAlpha)
        + float4(_PageCurlShadowColor.rgb * stuckShadowAlpha, stuckShadowAlpha);

    float4 exposedPageColor = glueMarkLayer * (1.0 - liftedShadowAlpha)
        + float4(_PageCurlShadowColor.rgb * liftedShadowAlpha, liftedShadowAlpha);
    float4 liftedRegionColor = exposedPageColor * (1.0 - liftedLowerAlpha)
        + float4(lowerArt.rgb * curlShade * liftedLowerAlpha, liftedLowerAlpha);

    float4 stickerColor = lerp(stuckRegionColor, liftedRegionColor, liftedMask);
    stickerColor = stickerColor * (1.0 - upperAlpha) + float4(upperColor * upperAlpha, upperAlpha);
    return stickerColor;
}

float4 EvaluateDoodleSticker(DoodleStickerSurface surface)
{
    DoodleStickerSamplingContext context;
    context.atlasUVRect = surface.atlasUVRect;
    context.stickerUVDdx = ddx(surface.stickerUV);
    context.stickerUVDdy = ddy(surface.stickerUV);
    context.atlasUVDdx = context.stickerUVDdx * surface.atlasUVRect.zw;
    context.atlasUVDdy = context.stickerUVDdy * surface.atlasUVRect.zw;
    context.tint = surface.tint.rgb;

    float2 stickerUVFootprint = abs(context.stickerUVDdx) + abs(context.stickerUVDdy);
    context.stickerEdgeSoftness = max(stickerUVFootprint, 1e-6);
    float edgeWidth = max(length(stickerUVFootprint * surface.curlSpaceScale) * 0.7071, 1e-6);

    float4 stickerColor;
    float2 peelVector = (surface.grabPoint - surface.dragPoint) * surface.curlSpaceScale;

    UNITY_BRANCH
    if (dot(peelVector, peelVector) < DOODLE_STICKER_MIN_PEEL_DISTANCE * DOODLE_STICKER_MIN_PEEL_DISTANCE)
    {
        fixed4 flatArt = SampleStickerArt(context, surface.stickerUV);
        stickerColor = float4(flatArt.rgb * flatArt.a, flatArt.a);
    }
    else
    {
        stickerColor = EvaluatePeeledSticker(context, surface, edgeWidth);
    }

    return stickerColor * (surface.tint.a * _StickerOpacity);
}

#endif
