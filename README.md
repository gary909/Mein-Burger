# Mein-Burger

<img width="377" height="61" alt="Screenshot 2026-11-21 at 11 11 13" src="https://github.com/user-attachments/assets/4b069b60-3fb7-498e-8269-b7becb23d742" />

An app to help you pass the German Citizenship test (Einbürgerungstest).

## Features

- **Complete catalogue**: 300 federal questions + 10 questions for each of the 16 Bundesländer = **460 questions**.
- **20 practice tests + a random exam**, each with the 33-question structure (18 democracy, 9 history, 3 society, 3 state), 60-minute timer and the official pass mark of **17/33**.
- **German and English** question text, answer options and explanations.
- **43 image questions** (coats of arms and Bundesland maps) with the original illustrations.
- **Progress tracking** (seen questions, coverage per pool, results per Bundesland/test) stored in the browser via `localStorage`.
- Single-page app, no framework and no build step. Works by opening `index.html` directly.

## Run

Open `index.html` in a browser. Keep the `data/` folder next to it:

```
index.html
data/
  questions.js      (generated question catalogue)
  images/           (43 illustrations)
```

No web server is required; `data/questions.js` is loaded as a classic script so it also works from `file://`.

## Data sources and attribution

- **Questions**: official BAMF catalogue “Gesamtfragenkatalog zum Test Leben in Deutschland und zum Einbürgerungstest”, **Stand 07.05.2025** (PDF version 23, SHA-256 `EDD3ED5E56A35241B6CC61AA0630482DC9E64498DB508A7D6892CCEE2EC707A9`).
  <https://www.bamf.de/SharedDocs/Anlagen/DE/Integration/Einbuergerung/gesamtfragenkatalog-lebenindeutschland.html?nn=284310>
- **Answers, English translations, explanations and images**: [abdullahbutt/leben-in-deutschland-test](https://github.com/abdullahbutt/leben-in-deutschland-test), version 2026-10-28. The translations and explanations are licensed under **CC BY-NC 4.0** (attribution, non-commercial use). Some questions are lightly reworded/simplified compared with the official PDF; the official PDF remains the authoritative wording.
- This is an independent learning app, not an official BAMF product. Use is intended for personal, non-commercial exam preparation.

## Project layout

| Path | Purpose |
|---|---|
| `index.html` | The whole app (HTML, CSS, JavaScript) |
| `data/questions.js` | Generated catalogue (`window.EINBUERGERUNG_DATA`) |
| `data/images/` | Illustrations for image-based questions |
| `data/summary.json` | Counts and topic distribution of the generated catalogue |
| `data/review.csv` | Human-readable proof sheet (question, options, answer, explanation) |
| `data/pdf-mismatches.csv` | Output of the comparison against the official PDF text |
| `tools/Convert/` | .NET console app that builds `data/questions.js` from the source dataset |
| `tools/Extract/` | .NET/PdfPig helper used to inspect and render the official PDF |
| `tools/compare-with-pdf.ps1` | Compares the generated catalogue with extracted PDF text |

`data/questions.js` is **generated** — do not edit it by hand. Change the source data or the converter instead.

## Regenerating the question data

1. Download the source dataset (`quiz-data.json`) and the images from the [dataset repository](https://github.com/abdullahbutt/leben-in-deutschland-test) into `tools/.cache/`.
2. Run the converter (requires the .NET SDK):

   ```powershell
   dotnet run --project tools/Convert -- tools/.cache/quiz-data.json data
   ```

   The converter validates counts (300 federal, 16 × 10 state), four options per question, answer indices, duplicate questions and the existence of every referenced image. It writes `data/questions.js`, `data/summary.json` and `data/review.csv`.

3. Optional: compare the generated questions with the official PDF text (requires `pdftotext`):

   ```powershell
   pdftotext -layout -enc UTF-8 gesamtfragenkatalog.pdf bamf.txt
   powershell -ExecutionPolicy Bypass -File tools/compare-with-pdf.ps1 -PdfText bamf.txt -Dataset tools/.cache/quiz-data.json
   ```

## TODO

- Review the questions where the source dataset rewords the official BAMF text and align them with the PDF wording where it matters.
- Review/expand explanations and English translations.
- Add a B1 vocabulary list (words such as *Grundgesetz*, *Bundestag*, *Mehrheit* …).
- Create an iPhone/Android app.
- Create a printed book: vocabulary overview, 10 tests, full questions.
