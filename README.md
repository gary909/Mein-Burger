# Mein-Burger

<img width="377" height="61" alt="Screenshot 2026-11-21 at 11 11 13" src="https://github.com/user-attachments/assets/4b069b60-3fb7-498e-8269-b7becb23d742" />

An app to help you pass the German Citizenship test (Einbürgerungstest).

## Features

- **Complete catalogue**: 300 federal questions + 10 questions for each of the 16 Bundesländer = **460 questions**.
- **Five clear sections** on the home page: Test machen, Landesfragen, Über den Test, Neue Wörter für B1 and Alle Fragen.
- **20 practice tests + a random exam**, each with the 33-question structure (18 democracy, 9 history, 3 society, 3 state), 60-minute timer and the official pass mark of **17/33**.
- **Landesfragen practice**: choose any Bundesland and practise all 10 state questions, untimed, with score and full review.
- **All 460 questions** in a searchable/filterable browser with the correct answers highlighted and explanations.
- **100 B1 vocabulary entries** (article/plural, English, example sentences) grouped by theme.
- **German and English** question text, answer options and explanations.
- **Full German/English interface**: the EN toggle in the header translates the whole UI (home, About, tests, states, vocabulary, browse, exam controls and results). Questions remain in German with the English translation underneath; vocabulary English translations are shown only in EN mode.
- **43 image questions** (coats of arms and Bundesland maps) with optimized illustrations.
- **Progress tracking** (seen questions, coverage per pool, results per Bundesland/test, state practice scores) stored in the browser via `localStorage`.
- Single-page app, no framework and no build step. Works by opening `index.html` directly.

## Run

Open `index.html` in a browser. Keep the `data/` folder next to it:

```
index.html
data/
  questions.js      (generated question catalogue)
  vocabulary.js     (100 B1 vocabulary entries)
  i18n.js           (German/English UI translations)
  overrides.json    (source corrections, including BB01)
  images/           (43 optimized illustrations)
```

No web server is required; `data/questions.js`, `data/vocabulary.js` and `data/i18n.js` are loaded as classic scripts so the app also works from `file://`.

### Runtime-only package

Create a clean copy containing only the files needed to run the app:

```powershell
powershell -ExecutionPolicy Bypass -File tools/package.ps1
```

This writes `dist/` (48 files, ~6.6 MB) and `mein-burger-runtime.zip` (~6.2 MB). Both are git-ignored. The packaged app still opens directly from `dist/index.html` with no server. The 43 illustrations are stored as optimized 8-bit grayscale PNGs (~6.1 MB total).

## Data sources and attribution

- **Questions**: official BAMF catalogue “Gesamtfragenkatalog zum Test Leben in Deutschland und zum Einbürgerungstest”, **Stand 07.05.2025** (PDF version 23, SHA-256 `EDD3ED5E56A35241B6CC61AA0630482DC9E64498DB508A7D6892CCEE2EC707A9`).
  <https://www.bamf.de/SharedDocs/Anlagen/DE/Integration/Einbuergerung/gesamtfragenkatalog-lebenindeutschland.html?nn=284310>
- **Answers, English translations, explanations and images**: [abdullahbutt/leben-in-deutschland-test](https://github.com/abdullahbutt/leben-in-deutschland-test), version 2026-10-28. **The German question and option wording is taken verbatim from the official BAMF PDF**; the dataset supplies the answer key, English translations, explanations and images. The translations and explanations are licensed under **CC BY-NC 4.0** (attribution, non-commercial use). `data/overrides.json` contains manual corrections for the few items where the dataset described a different paraphrase (e.g. B232, B247, HB02, SN07).
- **Redrawn images**: all 16 state coat-of-arms images (`data/images/301-*.png`) and all 16 state map images (`data/images/308-*.png`), plus `data/images/21.png`, `data/images/55.png`, `data/images/70.png`, `data/images/130.png`, `data/images/176.png`, `data/images/181.png`, `data/images/187.png`, `data/images/209.png`, `data/images/216.png`, `data/images/226.png` and `data/images/235.png`, are redrawn replacements added for this project; the remaining images come from the dataset.
- **BB01 answer correction**: the official BAMF page shows the Brandenburg eagle as **Bild 1**; `data/overrides.json` pins `correct: 0`, overriding the dataset's incorrect Bild 4 mapping.
- This is an independent learning app, not an official BAMF product. Use is intended for personal, non-commercial exam preparation.

## Project layout

| Path | Purpose |
|---|---|
| `index.html` | The whole app (HTML, CSS, JavaScript) |
| `data/questions.js` | Generated catalogue (`window.EINBUERGERUNG_DATA`) |
| `data/vocabulary.js` | 100 B1 vocabulary entries (`window.B1_VOCABULARY`) |
| `data/i18n.js` | German/English UI strings (`window.I18N`) |
| `data/overrides.json` | Source corrections and the BB01 answer fix |
| `data/images/` | Illustrations for image-based questions |
| `tools/package.ps1` | Builds the runtime-only `dist/` folder and zip |

`data/questions.js` is a generated, committed catalogue — do not edit it by hand. The generation and PDF-audit tooling has been removed from this minimal repository; `data/overrides.json` preserves the corrections that were applied.

## TODO

- Review the remaining wording differences between the dataset and the official PDF for English translation nuance (the German text is already official).
- Expand the B1 vocabulary list beyond the initial 100 words.
- Create an iPhone/Android app.
- Create a printed book: vocabulary overview, 10 tests, full questions.
