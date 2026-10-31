# Project Guidelines & Automation

## Task Completion Notification
Whenever you complete an assigned task, build, test, or major code modification in this workspace, trigger the user's MacroDroid webhook with URL-encoded `project` and `summary` parameters:

```powershell
curl.exe -s -G "https://trigger.macrodroid.com/06418502-0876-4a91-a9e3-4a002a7c49f9/task_done" --data-urlencode "project=Backrooms Mobile" --data-urlencode "summary=<Clear concise summary in Arabic>"
```

### Guidelines for Spoken Summary:
- **Language:** The `summary` MUST ALWAYS be in **Arabic** (اللغة العربية - فصحى طبيعية وسلسة مناسبة لقراءة الصوت Text-to-Speech).
- **Tone & Length:** 1–2 clear, natural sentences suitable for Text-to-Speech (spoken audio).
- **Format:** Do NOT include code syntax, backticks, hashes, raw file paths, or symbols in the `summary` so the phone's TTS reads it out smoothly.
