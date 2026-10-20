# Designed storm validation

Run 2026-10-20 00:06, Unity 6000.6.6f1.

| Test | Result | Detail |
| --- | --- | --- |
| same seed gives the same storm, another seed a different one | PASS | identical over 111 min; seed 43 differs in 667 slot-samples |
| environment: high shear + low CAPE is smaller and more tilted than high CAPE | PASS | low CAPE 611 J/kg, shear 39 m/s: R 1.684 km, tilt 0.36, top 9.1 km; high CAPE 4465, shear 20: R 3.8, tilt 0.081, top 13.3 |
| environment: High Plains dryline storm has a high base and exposed tornadoes; HP wraps them in rain | PASS | dryline: base 2.234 km, hpness -0.59, condensation reach 0.196, rain wrap 0; Dixie HP: base 0.684 km, hpness 0.995, reach 0.966, wrap 0.797 |
| climatology: EF mix and widths per region at the reference STP | PASS | Plains: EF0-5 0.604/0.267/0.086/0.033/0.007/0.001 vs 0.623/0.268/0.079/0.025/0.005/0.001; Dixie: EF0-5 0.384/0.453/0.119/0.034/0.009/0.001 vs 0.407/0.446/0.113/0.028/0.006/0.001; Midwest: EF0-5 0.478/0.393/0.096/0.027/0.005/0.001 vs 0.503/0.384/0.091/0.019/0.004/0 |
| climatology: higher STP shifts the mix toward strong tornadoes | PASS | EF2+ share at STP 0.5 / 1.5 / 6: 0.048 / 0.122 / 0.49 |
| cycles: 1-4 per storm, tornado count and gaps near the record, later cycles both weaker and stronger | PASS | cycles 1-4: 46/119/61/174; storms with 1/2/3/4 tornadoes 98/115/98/67 (2+ share 0.741, record ~0.34 at STP 1.5, more at this preset's STP 4.6); median gap 19 min (record 15); next tornado weaker 198/512, stronger 196/512; next updraft smaller 191/763 |
| geometry: clear slot and horseshoe grow with tornado size | PASS | 671 tornadoes: rank correlation of slot radius with funnel size 0.719; slot radius 3.586 km for the smallest fifth, 8.117 km for the widest |
| variety: one shape comes in many proportions; tornadoes change shape, skin and soil; satellites are rare | PASS | 1474 tornadoes: change shape 0.416, satellite 0.05, sidewind 0.088, laminar 0.117, translucent 0.079, lean > 100 m 0.44; cone flare spread (cv) 0.408, brightness 0.5-1.2; soils Plains: tan/red, Dixie: dark/red clay, Midwest: dark/tan |
| real time: storm and tornado move at the storm motion, wall cloud turns at v/r | PASS | storm 15.807 m/s = Bunkers 15.807; tornado 15.087 m/s, peak wind 48.701 m/s (EF1), funnel turning 1.81 rad/s; wall cloud turns once every 5.409-6.328 min |
| along-track environment: richer air turns an LP storm classic; a cap kills it early | PASS | LP to classic: hpness -1 -> 0, base 2.304 -> 1.261 km; into a cap: life 104 min vs 153 min in open air |
| kinematic wind: inflow below the base, updraft, counter-clockwise mesocyclone, outflow aloft | PASS | inflow 3.963 m/s at 6 km, updraft 38.2 m/s at 6 km, mesocyclone 20.759 m/s at 3 km radius, anvil outflow 15.983 m/s; 129x122x18 blocks |
| every preset evaluates over its whole life without NaN | PASS | plains Supercell 168 min 2c, hail Supercell 126 min 2c, dixie Supercell 136 min 2c, ohio Supercell 143 min 2c, lp Supercell 124 min 2c, capped Capped 240 min 0c, pulse Pulse 75 min 0c |

12 passed, 0 failed.
