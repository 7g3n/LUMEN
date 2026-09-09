# LUMEN — Balance Configuration

Every gameplay number is data, not code. Defaults live in an embedded
`balance.default.json` (shipped in `LUMEN.Core`); a `settings/balance.json` in the
data folder overrides individual keys for tuning without a rebuild.

These are **starting values** from the spec (§22–23, §29, §31). They will move during
Phase 3–4 tuning; the tests lock the *current* values so a change is always deliberate.

## Judgement windows (§22)

Symmetric, milliseconds from the perfect hit time.

| Judgement | Window   | Accuracy weight (§23) | Breaks combo (§24) |
|-----------|----------|-----------------------|--------------------|
| PERFECT   | ± 25 ms  | 100 %                 | no                 |
| GREAT     | ± 50 ms  | 90 %                  | no                 |
| GOOD      | ± 90 ms  | 60 %                  | no                 |
| BAD       | ± 140 ms | 20 %                  | yes                |
| MISS      | —        | 0 %                   | yes                |

`accuracy = Σ(weightᵢ) / (noteCount × 100%)`, expressed as a percentage.

## Hold notes (§17)

- Head judged with the table above.
- Tail judged with a separate (wider) window: `holdTailWindowMs` = ± 120 ms default.
- Releasing early past a grace of `holdBreakGraceMs` = 60 ms → the hold counts as BAD.

## Score (§25)

Kept separate from PP and Rating.

```
rawNoteScore   = weight(judgement)                       // 1.0 / 0.9 / 0.6 / 0.2 / 0
comboBonus     = clamp(currentCombo / comboBonusCap, 0, 1) * comboBonusWeight
score         += baseValue * (rawNoteScore + comboBonus)
final          = round(score / theoreticalMax * scoreScale)   // scoreScale default 1_000_000
```

`baseValue` is uniform per note in v1; `comboBonusCap` = 400, `comboBonusWeight` = 0.15,
`scoreScale` = 1_000_000. Displayed grades map from `final` (`A+` etc., thresholds in config).

## Rating (§29)

```
performanceRatings = player's best N per-chart performance ratings, desc     // N = ratingPoolSize (default 50)
weightᵢ            = ratingDecayBase ^ i            // i = 0..N-1, ratingDecayBase default 0.95
rating            = Σ(perfRatingᵢ * weightᵢ) / Σ(weightᵢ)
```

`performanceRating` per play (feeds the pool, distinct from PP):

```
perfRating = chart.difficultyLevel
           + accuracyBonus(accuracy)          // 0 at 100%, negative below
           - missPenalty(missCount)
           + fullComboBonus                   // +0.15 if FC
```

## PP (§31)

Per play, "how impressive was this one". Every function is pure and independently tested.

```
basePP              = ppBaseCurve(difficultyLevel)                 // ~ difficultyLevel^ppExponent * ppScale
accuracyMultiplier  = ppAccCurve(accuracy)                         // steep near 100%
comboMultiplier     = 0.5 + 0.5 * (maxCombo / noteCount)
missPenalty         = ppMissBase ^ missCount                       // ppMissBase default 0.97
technicalMultiplier = 1 + ppTechWeight  * norm(attributes.technical)
speedMultiplier     = 1 + ppSpeedWeight * norm(attributes.speed)
readingMultiplier   = 1 + ppReadWeight  * norm(attributes.reading)

finalPP = basePP
        * accuracyMultiplier
        * comboMultiplier
        * missPenalty
        * technicalMultiplier
        * speedMultiplier
        * readingMultiplier
```

Defaults: `ppExponent` = 2.4, `ppScale` = 0.28, `ppTechWeight` = 0.10,
`ppSpeedWeight` = 0.10, `ppReadWeight` = 0.08. `norm(x)` maps a skill attribute onto
roughly `[-1, 1]` around a reference level.

**Total PP** = weighted sum of best-N per-chart PP (same decay idea as Rating, own pool).
**Best PP** = single highest performance.

## Skill profile (§40)

```
axisValue(axis) = Σ over best performances P of
                    ( P.perfRating * P.chart.attributes[axis]_normalized * decay^rank )
                  / Σ ( decay^rank )
```

Five axes: Speed, Technical, Reading, Stamina, Accuracy.
