# Translator module

Optional manual translation through the official Yandex Cloud Translate v2 API. No shared key,
scraping, automatic translation while typing, or network request at startup. Text and results live
only in the view model; this module neither saves nor logs them. Opening the website sends no text.

## User setup

Settings → Translator accepts a **service-account API key**, with `yc.ai.translate.execute` scope
and `ai.translate.user` role. IAM/user tokens are not supported by this adapter. The service account's
own folder is used, so the request deliberately omits `folderId`. Enable consent and Save. Blank key
input retains the saved key; Disconnect cancels requests and removes the credential. Usage may incur
charges on the user's Yandex Cloud account. The UI links to setup, pricing, and the Cloud agreement.
No resource, account, subscription, or paid operation is created by the app.

The secret and consent live in the current user's Windows Credential Manager under
`DynamicIsland/YandexTranslate/v1`, using a non-roaming generic credential. They are absent from
`settings.json` and Git. The settings view uses a PasswordBox and never reloads the saved key into it.
Every API request checks saved consent again. Editing text or languages cancels a pending request;
generation checks discard late responses even from providers that ignore cancellation.

## API behavior

- Fixed HTTPS endpoint; redirects disabled to prevent forwarding credentials.
- 10,000 UTF-16 code-unit input cap (conservative for non-BMP text); plain text format.
- RU, EN, DE, FR, ES, ZH, plus automatic source detection.
- Manual Translate action, 15-second cancellation timeout, bounded 1 MiB response buffer.
- Sanitized authentication/quota/network/protocol errors; server error payloads are not surfaced.
- No retry that could silently duplicate billable requests. Clipboard copy is explicit.

## Official references verified 2026-11-03

- REST request/response and limits: https://aistudio.yandex.ru/en/docs/translate/api-ref/Translation/translate
- Authentication and service-account folder handling: https://aistudio.yandex.ru/en/docs/translate/api-ref/authentication
- Key scope/setup: https://aistudio.yandex.ru/en/docs/translate/quickstart
- Usage is metered by submitted characters; auto-detection in a translation request is included:
  https://aistudio.yandex.ru/en/docs/translate/pricing
- Cloud agreements depend on the customer's contracting entity:
  https://yandex.cloud/en/docs/billing/concepts/contract

No legal terms from the free consumer website are assumed to authorize API access. The module does
not embed a price because tariffs and customer currencies can change.

## Verification and remaining manual checks

Provider tests use a fake HTTP handler only. View-model tests cover manual-send behavior, stale
responses, swap, credential removal, and both localization resources. Windows Credential Manager,
live paid API access, focus behavior, scrollbars, clipboard, and 70–130%/DPI visual checks require
Windows and have not been validated by a Linux cross-build.
