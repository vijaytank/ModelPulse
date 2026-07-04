# Contributing to ModelPulse

Thank you for your interest in contributing to ModelPulse! We welcome community contributions. To maintain code quality and safety, please follow these guidelines.

---

## 🛠️ Development Workflow

1.  **Fork the Repository**: Create a personal fork of the repository on GitHub.
2.  **Create a Branch**: Create a feature branch off the `develop` branch.
    *   *Features*: `feature/your-feature-name`
    *   *Bug Fixes*: `bugfix/issue-description`
    *   *Docs*: `docs/document-name`
3.  **Local Development**:
    *   Keep changes focused and modular.
    *   Avoid changing configurations or endpoints in the source code directly. Use environment variable overrides instead.
4.  **Test First**:
    *   We follow a strict **Test-First/Regression-Test** requirement.
    *   Any new feature or bug fix **must** be accompanied by a corresponding unit test update in the `tests/ModelPulse.Tests/` project.
    *   Ensure all tests pass locally before committing:
        ```powershell
        dotnet test
        ```
5.  **Submit a Pull Request (PR)**:
    *   Target the **`develop`** branch (never merge directly into `main`).
    *   Clearly describe the problem, changes made, and verification steps.

---

## 📝 Pull Request Checklist

Before submitting your PR, ensure that:
- [ ] Code builds successfully without compiler warnings.
- [ ] All 75+ unit tests pass locally.
- [ ] New unit tests are added for any new logic or bug fixes.
- [ ] No production secrets, personal directories, or local absolute paths are hardcoded.
- [ ] Coding style conforms to C# standards (.NET 10.0 conventions).

---

## ⚖️ Code of Conduct

We expect all contributors to adhere to standard professional conduct, providing constructive, respectful reviews and interactions.
