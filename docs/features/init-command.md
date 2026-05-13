# Product Requirements Document — `init` Command

**Version:** 1.0  
**Date:** 2026-05-12  
**Status:** Draft — Awaiting Michael Approval  
**Author:** Luke (Lead)  
**Requested By:** Michael Prattinger  
**Branch:** `feat/init-command-prd`

---

## 1. Title

`init` Command for Generating `appsettings.json`

---

## 2. Overview

Add a new `init` command to CsvLoader that interactively scaffolds an `appsettings.json` file for either:

- the **current working directory** (project-local config), or
- the user's **global config directory** via `-g` (`~/.sqlapicli\appsettings.json`).

The command is intended to reduce setup friction for first-time users and make the existing configuration model discoverable without requiring manual JSON authoring.

This PRD covers **product behavior only**. No implementation work begins until Michael approves the specification and resolves the open questions in Section 9.

---

## 3. Goals

1. Provide a fast, guided way to create a valid `appsettings.json` file.
2. Align generated config with CsvLoader's existing configuration shape and precedence model.
3. Support both project-local and user-global configuration targets.
4. Preserve intentionally blank values in the generated JSON rather than omitting properties.
5. Keep the command simple enough that Han can implement it with low architectural risk and Leia can test it deterministically.

---

## 4. Requirements

### 4.1 Functional Requirements

- **FR-01:** CsvLoader SHALL expose a new subcommand: `init`.
- **FR-02:** Running `init` with no flags SHALL target `appsettings.json` in the current working directory.
- **FR-03:** Running `init -g` SHALL target `appsettings.json` in the user's global config directory: `~/.sqlapicli\appsettings.json`.
- **FR-04:** If the global config directory does not exist, the command SHALL create it before writing the file.
- **FR-05:** The command SHALL prompt interactively for these values, in this order:
  1. endpoint
  2. username
  3. password
  4. timeout
- **FR-06:** The endpoint prompt SHALL present the recommended default value `https://as400.becom.at:11443/api/v1/sql/raw`.
- **FR-07:** The username prompt SHALL have no default value.
- **FR-08:** The password prompt SHALL have no default value.
- **FR-09:** The timeout prompt SHALL present the recommended default value `20` seconds.
- **FR-10:** The generated JSON SHALL use the existing `CsvLoader` configuration section, not a new section name.
- **FR-11:** The generated file SHALL include all four properties even when one or more values are blank.
- **FR-12:** For string fields (`Endpoint`, `Username`, `Password`), an intentionally blank response SHALL be written as an empty string (`""`) rather than omitting the property.
- **FR-13:** The generated file SHALL be structurally equivalent to:

```json
{
  "CsvLoader": {
    "Endpoint": "https://as400.becom.at:11443/api/v1/sql/raw",
    "Username": "user@company.com",
    "Password": "secret",
    "Timeout": 20
  }
}
```

- **FR-14:** The command SHALL complete without making any network call.
- **FR-15:** On successful write, the command SHALL tell the user which path was written.
- **FR-16:** The command SHALL be additive only; it SHALL NOT modify query/export behavior.

### 4.2 Non-Functional Requirements

- **NFR-01:** The prompt flow SHALL remain simple and linear; no wizard-style multi-screen interaction.
- **NFR-02:** Password entry SHOULD be masked during input to avoid shoulder-surfing and terminal history exposure.
- **NFR-03:** The command SHALL work on Windows and Linux path conventions using the platform's home-directory resolution rules.
- **NFR-04:** The generated JSON SHALL be human-readable (indented formatting).
- **NFR-05:** The command SHALL write UTF-8 JSON.
- **NFR-06:** Failure modes (invalid input, permission issues, existing-file conflicts) SHALL produce clear, actionable error messages.
- **NFR-07:** The feature SHALL remain consistent with the current configuration precedence model: exe-dir < user-global `.sqlapicli` < user-secrets < current working directory < CLI args.

---

## 5. Sample JSON Outputs

### 5.1 Project-Local File (`init`)

**Target:** `./appsettings.json`

```json
{
  "CsvLoader": {
    "Endpoint": "https://as400.becom.at:11443/api/v1/sql/raw",
    "Username": "apiuser",
    "Password": "secret",
    "Timeout": 20
  }
}
```

### 5.2 User-Global File (`init -g`)

**Target:** `~/.sqlapicli/appsettings.json`

```json
{
  "CsvLoader": {
    "Endpoint": "https://as400.becom.at:11443/api/v1/sql/raw",
    "Username": "global-user",
    "Password": "global-secret",
    "Timeout": 20
  }
}
```

### 5.3 Blank String Values Preserved

```json
{
  "CsvLoader": {
    "Endpoint": "",
    "Username": "",
    "Password": "",
    "Timeout": 20
  }
}
```

> Note: Blank handling for `Timeout` remains an approval item because numeric defaults and "preserve empty input" semantics conflict unless we explicitly define the data contract.

---

## 6. Edge Cases

1. **`appsettings.json` already exists**  
   Current behavior is undefined for `init`. Approval needed on whether the command should:
   - overwrite without prompt,
   - warn and abort,
   - require explicit confirmation, or
   - merge existing JSON.

2. **Blank input vs. defaulted prompt**  
   Endpoint and timeout are described as having defaults, but Michael also requested that empty input preserve an empty value. These rules conflict unless we explicitly define whether pressing Enter means "accept default" or "store empty".

3. **Password visibility**  
   For safety and consistency with existing password prompting, the recommended behavior is masked input. Approval still requested.

4. **Non-numeric timeout**  
   The command needs a clear rule: reject and re-prompt, or accept and fail later. Recommended behavior: reject immediately and re-prompt.

5. **Invalid endpoint format**  
   The command may validate URI shape before writing, or accept any string and leave validation to runtime. Approval needed.

6. **Missing or inaccessible target directory**  
   The command must fail cleanly if it cannot create the global folder or write the file due to permissions.

7. **Interrupted interactive session**  
   If the user cancels before completion, the command should leave no partial file behind.

8. **Pre-existing unrelated JSON content**  
   If an existing file contains other sections, we need an approval decision on whether `init` is allowed to replace the full file or must preserve non-`CsvLoader` content.

---

## 7. Success Criteria

- A reviewed PRD exists at `docs/features/init-command.md`.
- The feature branch exists: `feat/init-command-prd`.
- The PRD clearly defines command scope, prompt order, target paths, JSON shape, and testable outcomes.
- Han can implement the command without inventing new product behavior.
- Leia can derive unit and integration test cases directly from the requirements and edge cases.
- No implementation code is written before Michael approves the PRD.

---

## 8. Recommended Implementation/Test Split (Post-Approval)

- **Han:** CLI surface, path resolution, prompt flow, JSON write behavior, overwrite policy implementation.
- **Leia:** prompt/validation tests, target-path tests, overwrite/abort tests, blank-value serialization tests, cross-platform path resolution tests.

---

## 9. Approval Questions for Michael

Please confirm the following before implementation starts:

1. If `appsettings.json` already exists, should `init` overwrite, abort, prompt for confirmation, or merge?
2. Should password input be masked?
3. Should the command validate endpoint URL format before writing?
4. Should non-numeric timeout input re-prompt immediately?
5. Is the global directory always `~/.sqlapicli`, or should it be configurable?
6. For endpoint and timeout prompts, does pressing Enter mean **use the displayed default** or **persist an empty value**?
7. If an existing `appsettings.json` has other sections, must `init` preserve them?
