Interface translations are stored in separate UTF-8 JSON files:

- `en-US.json`: English (the fallback language)
- `zh-CN.json`: 简体中文

Both files use matching stable keys. Edit the values to change the displayed text. Keep numbered placeholders such as `{0}` and `{1}` intact, and preserve their meaning when rearranging a sentence. New entries must be added to both files.

The language menu switches the interface immediately and saves the selection in `settings.json`. Language files are copied into the published application's `Languages` folder. Game names use their saved English or Chinese fields; descriptions entered by the user, file paths, version keys, and DOSBox configuration values are not translated.
