# Test strategy examples

## Agent-backed executor

Split the executor from the semantic dependency where practical:

```text
Workflow executor
   -> ITranslator / AIAgent adapter
   -> typed TranslationResult
```

Test executor transition logic with a fake semantic result. Test the actual agent prompt/model separately.

## Tool

Test:

- schema/argument validation;
- authorization;
- cancellation;
- timeout/failure mapping;
- idempotency;
- redacted/bounded return shape.

## Context provider

Test which context is selected/injected for known inputs without invoking the model.

## Session

Test conversation isolation, persistence/restore semantics, expiration if application-owned, and absence of cross-user leakage.
