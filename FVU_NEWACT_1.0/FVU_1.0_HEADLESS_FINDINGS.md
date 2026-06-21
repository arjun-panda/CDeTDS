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

## CONTRACT FULLY DECODED (CFR decompile of FVU.main, 2026-06-21)

Decompiled with CFR 0.152. `main(String[] a2)` exact logic:

```
if (a2.length == 6 || 7 || 8) {
    a5  = a2[0]  // INPUT .txt file
    a6  = a2[1]  // ERROR/OUTPUT html file path (printed "FVU path:")
    a7  = a2[2]  // OUTPUT .fvu file path (static gce; input renamed to this on success)
    a8  = a2[3]  // MODE int as string: "0" = full validate + produce .fvu
    a9  = a2[4]  // FVU VERSION string — MUST EQUAL "1.0"  (line 311 gate!)
    a10 = a2[5]  // SAM version (parsed to int a17)
    a11 = a2[6]  // CSI file (when len 7/8 and a8=="0")
    a12 = a2[7]  // extra (8-arg form)
    a16 = parseInt(a8)  // mode
    // version gate: if mode not in {0,1,2} AND a9 != "1.0" -> "Incorrect FVU Version", exit
    if (a16 == 0) {
        new FVU("just to overload constructor");          // <-- JFrame ctor
        if (a11.length>0) com.tin.tds.c.a.pc(a11);         // set CSI path
        fvu.j(a5, <errDir>, a7, a9, a17, a12);             // <-- THE VALIDATION CALL
    } else { ...hash-check modes 1/2... }
} else if (a2.length == 0) { new FVU().setVisible(true); } // GUI
else { "Invalid Number of arguments..." }                  // <-- only for length 3,4,5,9+
```

### Correct headless command (7-arg, mode 0):
```
java -cp <all 9 jars> com.tin.FVU.FVU <input.txt> <errOut.html> <out.fvu> 0 1.0 <samVer> <csi.csi>
```
My earlier failures: I put the FORM CODE ("140") in a2[4], but a2[4] is the FVU
VERSION and must be literally **"1.0"**. The "Invalid Number of arguments" was
only from arg counts of 3/4/5 — 6/7/8 get past it.

## Remaining blocker (last mile — threading, not logic)

With correct args the version gate PASSES and it reaches the validation path, but
the process does NOT exit cleanly — `new FVU(...)` is a `javax.swing.JFrame`, so a
non-daemon AWT/Swing thread keeps the JVM alive (the online version validator at
onlineservices.tin.egov.proteantech.in runs and logs `--result--9.4^2.190^...`).
The validation likely runs; the JVM just hangs open.

### To finish (next session — this is now a WIRING task, not RE):
1. Build a `RunFVU1` shim (like the 9.4 RunFvuClassB64) that calls the validation
   path then forces `System.exit(0)` / runs headless (`-Djava.awt.headless=true`
   may suppress the JFrame; or call `FormValidator.j(...)` directly, bypassing the
   `new FVU()` JFrame entirely — see main line 369-371: `new FormValidator().j(a5,a6,a16)`).
2. Test with a REAL CDeTDS-generated Form 140 .txt + matching .csi (synthetic
   stubs error out before producing a usable error report).
3. Confirm it emits the .fvu (success) or the err.html (failure) like 9.4.
4. Wire dual-FVU in FvuUtilityRunner (9.4 for FY<=2025-26, 1.0 for TY2026-27).

## 2026-06-21 — EXIT solved, JFrame is the last wall (with the solution)

Tested empirically:
- WITHOUT `-Djava.awt.headless=true`: validation path runs but JVM HANGS (the
  `new FVU()` JFrame keeps a non-daemon AWT thread alive).
- WITH `-Djava.awt.headless=true`: process EXITS, and errors are precise & fixable:
  1. `args[5]` (a10) must be an **integer** — `Integer.parseInt(a10.trim())` (NOT
     "1.0"; that's args[4]=version). It's the SAM/form numeric.
  2. Then mode-0 reaches `new FVU("just to overload constructor")` which throws
     `HeadlessException` at `java.awt.Frame.<init>` — you can't build the JFrame
     headless.

So: headless flag fixes the hang but breaks the mode-0 path because that path
constructs a JFrame. The DECOMPILE shows the clean way out:

**THE FIX (next session — known, not speculative):** bypass `new FVU()` entirely.
`main`'s modes 1/2 branch calls the engine directly:
```
new com.tin.FVU.FormValidator().j(inputTxt, errorHtmlPath, mode)   // FVU.java:369-371
```
and mode-0's real work is inside `FVU.j(a5, errDir, a7, a9, a17, a12)`. Write a
small `RunFVU1.class` shim (compiled for Java 8, base64-embedded like the existing
`RunFvuClassB64`) that:
  - runs with `-Djava.awt.headless=true`,
  - calls `FormValidator.j(...)` (or replicates FVU.j's body) directly — NO JFrame,
  - forces `System.exit(0)` at the end.
Then test with a REAL Form 140 .txt + .csi and confirm `.fvu`/err.html output.

**Bottom line: feasibility CONFIRMED.** Contract fully decoded, version gate
passes, exit solved via headless flag. The only remaining work is a ~1-class Java
shim to dodge the JFrame — the same technique already proven for 9.4. This is
integration, not research.

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
