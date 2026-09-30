# Play Console: App content declarations

Checklist for `com.qbtapp.lensapp`, moved here from the qbtapp.com site project. The answers
below follow from what the app does today (manifest, code and the privacy policy). They are
proposals: each declaration is an attestation made by the account holder, so Luca enters and
submits them in Play Console (Policy and programs → App content).

Sources of truth: `src/LensApp/Platforms/Android/AndroidManifest.xml`, and the privacy policy at
<https://qbtapp.com/lensapp/privacy/> (source: `src/pages/lensapp/privacy.astro` in the
qbtapp.com repo). Data safety and the policy must say the same thing; if permissions or SDKs
change, update both.

## Already done (21/08/2026)

| Declaration | Answer |
| --- | --- |
| Ads | The app contains no ads |
| Sign-in details | All functions available without special access |
| Government apps | Filled in |

## To do

| # | Declaration | Proposed answer |
| --- | --- | --- |
| 1 | Privacy policy | URL `https://qbtapp.com/lensapp/privacy/` |
| 2 | Data safety | *Does the app collect or share any of the required user data types?* **No.** Nothing leaves the device: no `INTERNET` permission, no analytics, no account. Frames are processed in memory; *Save* writes a photo to the gallery locally, which Google does not count as collection |
| 3 | Content rating (IARC) | Category **Utility / Productivity**. Answer *No* to violence, sexual content, language, controlled substances, gambling, user-generated content, sharing of location, and unrestricted web access. Expected rating: lowest (3+ / Everyone) |
| 4 | Target audience | **18 and over**, so the app stays out of the Families programme. It is a tool, not aimed at children |
| 5 | Financial features | **None** (no payments, loans, crypto, banking) |
| 6 | Health | **None**. Colour and magnification only, no health or medical features |
| 7 | Not identified | The earlier handoff counted 7 missing items but named only 6. Open App content in Play Console to see the seventh. Likely candidates are *Advertising ID* (answer **No**, the app does not use it) and *News apps* (answer **not a news app**) |

## Before filing

- Permissions in the manifest must match the Data safety form: `CAMERA`, and
  `WRITE_EXTERNAL_STORAGE` with `maxSdkVersion="28"`. No `INTERNET`, no
  `READ_MEDIA_IMAGES` or `READ_EXTERNAL_STORAGE`.
- After uploading the next bundle (source is at 1.3, versionCode 9; Play has 1.2), confirm in
  Test and release → App bundle explorer → Details → Permissions that no library added
  `INTERNET`.
- The store listing contact email is `support@qbtapp.com` (forwards to Luca's Gmail).

## After the declarations

The closed test (12 testers, 14 consecutive days) is still the step that unlocks production.
