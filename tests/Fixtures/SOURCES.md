# Test fixture sources

Fixtures are Apple Vision landmarks (`scripts/extract-pose-landmarks.swift`), never video. Each line is one analyzed frame.

| Fixture | Source | License | Segment |
|---|---|---|---|
| `face-nasa-menon` | [NASA Astronaut Candidate Anna Menon Resource Reel](https://images.nasa.gov/details/jsc2025m000153_NASA_Astronaut_Candidate_Anna_Menon_Resource_Reel), NASA/JSC | Public domain (NASA work) | 83.2–125.2 s, 600×600 crop at (660, 0), every 2nd frame |
| `head-niosh-sudha` | [Science Speaks: Talking to Women in Science (Sudha, Medical Officer)](https://commons.wikimedia.org/wiki/File:Science_Speaks_-_Talking_to_Women_in_Science_(Sudha,_Medical_Officer).webm), NIOSH/CDC | Public domain (US government work) | 46.9–69.6 s, every 2nd frame |
| `body-cdc-squat` | [Muscle Strengthening at Home: Half squat](https://commons.wikimedia.org/wiki/File:Muscle_Strengthening_at_Home_-_Half_squat.webm), CDC | Public domain (US government work) | 16.6–35.9 s, every 2nd frame |
| `upper-cdc-press` | [Muscle Strengthening at Home: Overhead Press](https://commons.wikimedia.org/wiki/File:Muscle_Strengthening_at_Home_-_Overhead_Press.webm), CDC | Public domain (US government work) | 0.2–37.8 s, every 2nd frame |
| `mirror-landmarks`, `turning-landmarks`, `face-*` (numbered) | Mixkit stock clips | Mixkit license: only these derived landmarks are stored; the clips are never committed | see `MirrorTests` |

The people shown do not endorse this project.
