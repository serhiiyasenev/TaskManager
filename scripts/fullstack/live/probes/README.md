# Reviewed native Azure probes

No concrete live probe is supplied in this directory yet. The executor and
protocol are implemented; real deployment, account/mailbox and native adapter
access have not been supplied. Missing any of the nine scripts/access inputs
returns SKIP2 before a network call. This is not nine-check acceptance or a
cloud PASS.

A reviewed implementation must have the exact `<check>.ps1` name listed in
[the runbook](../../../../docs/fullstack-verification.md#native-probe-protocol),
have its reviewed SHA256 in the private live configuration, and accept only:

```powershell
param([Parameter(Mandatory)][string] $ContextPath,
      [Parameter(Mandatory)][string] $ResultPath)
```

It must perform the specified actual candidate/native operation, keep secrets
in memory/private inputs, clean up its explicitly owned test data/resources,
exit nonzero for failed/missing/uncertain evidence, and write the fresh strict
aggregate artifact/result protocol. Presence of metadata, successful CLI login,
anonymous health, test fixtures or a generic `Passed` dictionary is insufficient.
The implementation/access routes require review before opt-in execution.
