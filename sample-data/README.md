# Sample data (synthetic)

These five CSV files are **made-up test data** in the same format as the Open University
Learning Analytics Dataset: 3 course runs, 24 students (one enrolled twice), 15 assessments,
and about 70 results, including the dataset's quirks (quoted text, `?` for missing values,
Windows line endings).

They let the automated tests (GitHub Actions) run in seconds without downloading the real
dataset. They are **not** real students and should not be shown as real data.

To run the app on this sample instead of the real dataset:

```bash
DATA_DIR=./sample-data RESET_DB=1 docker compose up --build
```
