# FVU 1.0 Headless Invocation — Reverse-Engineering Findings (2026-06-21)

Goal: drive Protean's FVU 1.0 (TY 2026-27) validator headlessly (no GUI), the way
CDeTDS already drives FVU 9.4, so new-Act returns can be auto-validated.

## TL;DR — feasibility

**Headless mode EXISTS and is reachable. Not yet fully cracked.** `FVU.main()`
has an explicit headless branch; it runs without a GUI and exits cleanly (no
network wall). What's left is the exact inner argument contract — an inner check
throws "Invalid Number of arguments passed in Main()" for the arg combinations
tried so far.

## Confirmed facts (with evidence)

1. **It is GUI-by-default, headless-on-args.** The bundled launcher is
   `start javaw -jar TDS_STANDALONE_FVU_1.0.jar` (no args → GUI). The embedded
   help (`com/tin/FVU/help.txt`) describes only a 3-field GUI input screen.

2. **`com.tin.FVU.FVU` extends `javax.swing.JFrame`** (it IS the GUI window) but
   its `public static void main(String[])` branches on arg count:
   ```
   if (args.length == 6 || == 7 || == 8) -> headless validation path (offset 68)
   else                                   -> launch GUI (offset 1786)
   ```
   (decompiled via `javap -p -c com.tin.FVU.FVU`.)

3. **Validation entry method exists**:
   `public void com.tin.FVU.FVU.j(String, String, String, String, int, String)`
   plus engine class `com.tin.tds.FormValidator`.

4. **Decoded arg slots in main's headless branch** (offsets 71-260):
   - `args[0]` -> input .txt File (local 2; `new File(args[0])` @239)
   - `args[1]` -> printed "FVU path:" => the FVU installation directory
   - `args[2]` -> static field `gce` + local 4; `new File(args[2])` @249
   - `args[3]` -> local 5; compared to the string "0" (the flag, like 9.4's "0")
   - `args[4]` -> local 6
   - `args[5]` -> local 7
   - when len==7 & args[3]=="0": `args[6]` -> CSI (local 8)
   - when len==8 & args[3]=="0": `args[6]`.trim() -> CSI, `args[7]` -> local 9
   - prints "--TO CHECK FVU VERSION-- 4.5"; record separator is caret `^`.

5. **No network block.** A headless run reached the inner logic and EXITED
   cleanly (earlier "still running" cases were the GUI from wrong arg counts).
   `VersionValidator.jar` contacted `onlineservices.tin.egov.proteantech.in` and
   logged `--result--9.4^2.190^2.1320` but did not hang. So the online
   version-check is NOT a hard blocker (at least with connectivity).

## Current blocker

An inner check in `FVU.class` throws **"Invalid Number of arguments passed in
Main()"** for the 6/7/8-arg combos tried (input, fvuPath, input2, "0", form,
qtr, csi). The outer length check passes (6/7/8) but a second/inner validation
rejects the specific values/positions. Need to decompile the inner logic
(method `t()` @offset 68 and the throw site) to get the exact contract.

## How to finish (next session)

1. Use a real Java decompiler (CFR / Procyon / Fernflower) on `FVU.class` for
   readable Java of `main`, `t()`, and `j(...)` — `javap -c` bytecode is enough
   to confirm structure but readable source will pin the arg semantics fast.
2. Compare to the WORKING 9.4 invocation in `FvuUtilityRunner.cs`:
   `java -jar FVU_9.4.jar <input> ver.txt <formCode> <quarter> 9.4 0 <csiName>`
   (7 positional args). FVU 1.0's slots differ (args[1] = FVU path is the new
   one); map 1.0's positions onto this.
3. Get a REAL generated Form 140 `.txt` (from CDeTDS) + a matching `.csi` to
   test against — synthetic stubs won't pass format checks far enough to confirm.
4. Build a `RunFVU` shim for 1.0 (mirror the base64 `RunFvuClassB64` approach in
   FvuUtilityRunner.cs) ONLY if reflection presets are needed; otherwise a plain
   `java -cp <all jars> com.tin.FVU.FVU <args>` may suffice once args are right.
5. Wire dual-FVU selection in FvuUtilityRunner: 9.4 for FY<=2025-26, 1.0 for
   TY 2026-27 (138/140/143/144). Bundle the 1.0 folder in the installer.

## Tooling notes
- `javap` present: JDK 17 at `C:\Program Files\Microsoft\jdk-17.0.13.11-hotspot`.
- Unpacked jar: `FVU_NEWACT_1.0/fvu_unpack/` (gitignored).
- Extracted FVU folder (run from here): `FVU_NEWACT_1.0/fvu_extracted/...`.
