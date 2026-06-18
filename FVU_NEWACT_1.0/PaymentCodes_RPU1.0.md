# IT Act 2025 Payment Codes — extracted from Protean RPU/FVU v1.0

⚠ **RELEASED UTILITY, but CODES STILL UNVERIFIED.** As of 2026-06-18 Protean's
download page lists RPU 1.0 and FVU 1.0 for TY 2026-27 as regular downloads with
NO "Beta/draft/provisional" qualifier — so the utility itself appears final (the
earlier "BETA" wording here was our own annotation, not Protean's). HOWEVER the
authoritative source for the codes — Protean's official file-format / data-
structure spec for the NEW forms (138/140/143/144/141) — is **NOT yet published**
(the page still lists file-format .xls only for the OLD forms 24Q/26Q/27EQ/27Q).
The ~18 `⚠` codes below therefore remain UNVERIFIED. `FVU_USE_PAYMENT_CODES`
stays OFF until they are confirmed from the published spec (or a decompile of the
FVU validator — see "2026-06-18 code-extraction attempt" below).

Source: `RPU Version_1.0_for TDSTCS Statements_2026-27 onwards.zip` downloaded
2026-06-12 from tinpan.proteantech.in. Codes and descriptions read from the
RPU's embedded form master (com/tin/etbaf/rpu/o.class). The raw string dump is
in `rpu_codes_ordered.txt`.

LEGEND: ✅ = unambiguous, seeded into CDeTDS built-in rules.
⚠ = ambiguous / needs the official data-structure document — left blank in rules.

## Form 138 (salary — replaces 24Q)
| Code | Description | Legacy |
|------|-------------|--------|
| 1001 | Payment to Government Employees other than Union Government Employees | 192 (State Govt) ⚠ category-dependent |
| 1002 | Payment of Employees other than Government Employee ("192 - 92B") | 192 (non-Govt) ✅ default |
| 1003 | Payment to Indian (Union) Government employees | 192 (Union Govt) ⚠ category-dependent |
| 1032 | Payment to Specified Senior Citizen | 194P ✅ |

## Form 140 (resident non-salary — replaces 26Q)
| Code | Description | Legacy |
|------|-------------|--------|
| 1004 | Accumulated PF balance due to an employee | 192A ✅ |
| 1005 | Commission or brokerage — insurance | 194D ✅ |
| 1006 | Commission or brokerage — others | 194H ✅ |
| 1008 | Rent on machinery etc. — specified person (194I(a)) | 194I P&M ✅ |
| 1009 | Rent other than machinery — specified person (194I(b)) | 194I L&B ✅ |
| 1011 | Consideration under agreement referred to in section 67(14) | 194IC ✅ |
| 1012 | Compensation on acquisition of certain immovable property | 194LA ✅ |
| "94K" | Units of specified Mutual Fund (Schedule VII) — RPU shows literal "94K" | 194K ⚠ (RPU oddity; expected 1013) |
| 1014 | Interest from units of business trust (194LBA(a)) | 194LBA ⚠ |
| 1015 | Dividend from units of business trust | 194LBA ⚠ |
| 1017 | Income re units of investment fund (section 224) | 194LBB ⚠ |
| 1018 | Income re investment in securitisation trust (section 221) | 194LBC ⚠ |
| 1019 | Interest on securities | 193 ✅ |
| 1020 | Interest other than securities — senior citizen | 194A (senior) ✅ |
| 1021 | Interest other than securities — other than senior citizen | 194A ✅ |
| 1022 | Interest other than interest on securities (generic) | 194A ⚠ overlap with 1021 |
| 1023 | Contract work — contractor is individual or HUF | 194C (Ind/HUF) ✅ |
| 1024 | Contract work — others (description follows 1023 pattern) | 194C (others) ✅ |
| 1026 | Fees for technical services / cinema royalty / call centre | 194J (2% leg) ⚠ |
| 1025 | (absent from RPU dropdown strings — likely 194J professional) | 194J ⚠ DO NOT GUESS |
| 1027/1028 | Director remuneration (other than section 392) | 194J(ba) ⚠ |
| 1029 | Dividends (including preference shares) | 194 ✅ |
| 1030 | Sum under a life insurance policy | 194DA ✅ |
| 1031 | Purchase of goods | 194Q ✅ |
| 1033 | Benefit or perquisite of business/profession | 194R ✅ |
| 1034 | Benefit/perquisite in kind (tax paid before release) | 194R (kind) ⚠ |
| 1035 | E-commerce participant sales via operator | 194O ✅ |
| 1037 | VDA transfer by other than individual/HUF | 194S ⚠ |
| 1038 | VDA transfer (cash/kind) | 194S ⚠ |
| 1058 | Winnings — lottery/crossword/cards/gambling | 194B ✅ |
| 1059 | Winnings in kind (tax paid before release) | 194B (kind) ⚠ |
| 1060 | Winnings from online game | 194BA ✅ |
| 1061 | Winnings online — in kind | 194BA (kind) ⚠ |
| 1062 | Winnings from horse race | 194BB ✅ |
| 1063 | Lottery ticket commission/remuneration/prize | 194G ✅ |
| 1064 | Cash payment by bank/PO/co-op to co-operative society | 194N (co-op) ⚠ |
| 1065 | Cash payment by bank/PO/co-op to others | 194N ✅ default |
| 1066 | Amount referred to in 80CCA(2)(a) of IT Act 1961 | 194EE ✅ |
| 1067 | Salary/remuneration/commission/bonus/interest to partner | 194T ✅ |

Note: 194IA / 194IB / 194M do not appear — they move to the consolidated
challan-cum-statement Form 141, not Form 140.

## Form 143 (TCS — replaces 27EQ): codes 1068–1092
1068 liquor · 1069 tendu leaves · 1070 timber (forest lease) · 1071 timber (other)
· 1072 other forest produce · 1073 scrap · 1074 coal/lignite/iron ore ·
1075 motor vehicle · 1076 wrist watch · 1077 art/antiques · 1078 collectibles ·
1079 yacht/helicopter · 1080 sunglasses · 1081 handbag/purse · 1082 shoes ·
1083 sportswear/golf kit · 1084 home theatre · 1085 race/polo horse ·
1086 LRS education/medical · 1087 LRS other · 1088 tour package ≤ threshold ·
1089 tour package > threshold · 1090 parking lot · 1091 toll plaza · 1092 mine/quarry

## Form 144 (non-resident — replaces 27Q): codes 1039–1057
PF balance, investment-fund/securitisation income, section 211 income,
foreign-currency/rupee-bond interest (1042–1045), IDF interest (1046),
business-trust income (1047), investment fund (1050), securitisation (1051),
MF units (1052), units sec 208 (1053), GDR interest/dividends (1054), GDR LTCG
(1055), FII securities (1056), other sums chargeable — old 195 (1057).
Per-row mapping needs the official data-structure document (27Q unsupported in
CDeTDS anyway).

## Open items (need the official data structure document / FVU 1.0 testing)
1. Code 1025 missing from dropdown strings (likely 194J professional) — confirm.
2. "94K" literal for 194K — confirm whether 1013 exists.
3. Whether FVU 1.0 changes FH/BH/CD/DD field layout beyond section→code
   substitution. Ground truth: run generated files through the bundled
   `TDS_STANDALONE_FVU_1.0.jar` (in this folder).

## 2026-06-18 re-check (re-download of RPU/FVU v1.0)
- The RPU and FVU v1.0 zips re-downloaded 2026-06-18 are **byte-for-byte
  identical** (verified SHA-256) to the 2026-06-12 Beta already analysed. This
  is the SAME build — NOT a newer/final release. The ~18 `⚠` codes above remain
  unresolved; nothing new to seed.
- **FVU 1.0 has NO headless CLI.** Its launcher (`TDS_STANDALONE_FVU_1.0.bat`)
  is literally `start javaw -jar TDS_STANDALONE_FVU_1.0.jar` — a GUI-only Swing
  app with no input-file/form/quarter/version/CSI arguments. By contrast CDeTDS
  drives FVU 9.4 headlessly as:
  `java -jar FVU_9.4.jar <input> ver.txt <formCode> <quarter> 9.4 0 <csiName>`.
  **Dual-FVU wiring is therefore BLOCKED**: there is no known way to invoke FVU
  1.0 non-interactively, parse its error report, and surface the .fvu file the
  way FvuUtilityRunner does for 9.4. Wiring it would require reverse-engineering
  the Beta jar's main class / CLI entry point (cf. the existing RunFVU.class
  patch + fvu_*.py probes done for 9.4) — and the Beta may expose no CLI at all.
- Status unchanged: `FVU_USE_PAYMENT_CODES` stays **OFF**; bundled FVU stays
  **9.4**. The FVU 1.0 jar is staged in `fvu_extracted/` for reference only.
- To genuinely "make final" still requires: (a) Protean's FINAL non-Beta
  RPU/FVU + official data-structure document, (b) resolving the `⚠` codes from
  that doc, (c) a headless-capable FVU (or reverse-engineered CLI), (d) a real
  Form 140 validated against it. None of these are satisfied by this re-download.

## 2026-06-18 code-extraction attempt (from the released jars)
Goal: recover authoritative codes from the released binaries since the .xls spec
is unpublished. Methods tried and results:
- **RPU dropdown strings** (`rpu_codes_ordered.txt`, from `o.class`): same as the
  original extraction — shows the literal `94K` oddity, and NO `1025` / NO `1013`.
  Released RPU is byte-identical to the 12-Jun build, so no new info.
- **FVU validator classes** (`com/tin/tds/a/r.class`, `h.class`, ~184 KB each):
  a byte-scan for `1025` matches inside these classes, suggesting the FVU DOES
  validate 1025 — but the codes are stored as integer constants in bytecode, not
  as plaintext string literals (a printable-string scan of r.class yielded only
  21 strings, none of them 4-digit codes). String-scraping therefore CANNOT
  produce a reliable, complete code list.
- **Conclusion:** authoritative codes still cannot be confirmed without either
  (a) Protean publishing the new-form file-format .xls, or (b) a proper Java
  decompile of the FVU validator classes (no decompiler bundled; not installed).
  Switch stays OFF. The unpacked jar lives in `fvu_unpack/` (gitignored) if a
  future session wants to decompile `com/tin/tds/a/r.class`.

## 2026-06-18 RPU "Select Section for PAYMENT" picker (CONFIRMED mappings)
Transcribed from the RPU 1.0 in-app section picker, which shows the official
Code → IT-2025 parent section → legacy IT-1961 section. This is the RPU's own
table (stronger than the dropdown-string scrape, but still NOT the canonical
file-format .xls). Mappings CONFIRMED from the screenshot:

| Code | Description (RPU) | IT 2025 | IT 1961 |
|------|------------------|---------|---------|
| 1005 | Commission or brokerage - insurance | 393(1) | 194D |
| 1006 | Commission or brokerage - others | 393(1) | 194H |
| 1008 | Rent - Machinery etc. | 393(1) | 194I |
| 1009 | Rent other than machinery etc | 393(1) | 194I |
| 1019 | Interest on securities | 393(1) | 193 |
| 1022 | Interest other than interest on securities | 393(1) | 194A |
| 1023 | Contractor - Individual or HUF | 393(1) | 194C |
| 1024 | Contractor - Other Than Individual or HUF | 393(1) | 194C |
| 1026 | Technical Services, Royality, Call Centre | 393(1) | 194J |
| 1027 | Fees for Professional Services | 393(1) | 194J |
| 1028 | Director Payment | 393(1) | 194J |
| 1029 | Dividends | 393(1) | 194 |
| 1031 | Purchase of Goods | 393(1) | 194Q |
| 1033 | Perquisite | 393(1) | 194R |
| 1034 | Perquisite in cash or in kind | 393(1) | 194R |
| 1035 | Sale of goods Ecommerce Operator | 393(1) | 194O |
| 1037 | Virtual Assets - other than Individual or HUF | 393(1) | 194S |
| 1038 | Virtual Assets | 393(1) | 194S |
| 1058 | Winnings | 393(3) | 194B |
| 1059 | Winnings (in kind) | 393(3) | 194B |
| 1060 | Online Games | 393(3) | 194BA |
| 1061 | Online Games (in kind) | 393(3) | 194BA |
| 1067 | Partners Payment | 393(3) | 194T |

### Key corrections vs the earlier dropdown-string extraction
- **194J is split across THREE codes:** 1026 = Technical Services/Royalty/Call
  Centre (the 2% leg), **1027 = Fees for Professional Services** (the 10% leg),
  1028 = Director Payment. Earlier notes worried "1025 = 194J professional, DO
  NOT GUESS" — that was WRONG. Professional fees is **1027**; the picker skips
  1025 entirely (1024 → 1026), so **1025 appears not to exist as a payment code**.
- 1037/1038 = 194S, 1034 = 194R-in-kind, 1059/1061 = in-kind winnings —
  previously `⚠`, now confirmed.
- 393(1) vs 393(3): winnings (1058–1061), partners' payment (1067) file under
  **393(3)**; the rest of the above under **393(1)**.

### Still NOT covered by this screenshot (need full picker / .xls)
- Salary codes (1001–1003, 1032) — Form 138 picker, not shown here.
- 192A/PF (1004), life insurance (1030), business-trust/investment-fund/
  securitisation (1014–1018), MF units / "94K" (1013?), 194N cash (1064/1065),
  194EE (1066), e-commerce variants, all TCS codes (1068–1092), all 27Q/144
  codes (1039–1057). These were not on the visible page.
- Whether the .xls assigns different codes than the picker. The published
  file-format .xls (e.g. "File Format for Form Number 138/140 ... Version 1.0")
  remains the canonical source and supersedes this if they ever disagree.

Status unchanged: `FVU_USE_PAYMENT_CODES` stays **OFF**. These confirmed mappings
are recorded for seeding once the full set (all forms) is captured and the .xls
cross-checked.
