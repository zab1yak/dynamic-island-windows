"""Fail if the music/update hotfix changes unrelated production code or UI resources."""
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

BASE = 'v0.3.2'
ALLOWED = {
    'src/DynamicIsland.Modules.Media/Views/MediaTemplates.xaml',
    'src/DynamicIsland.App/Services/UpdateService.cs',
    'src/DynamicIsland.App/Services/UpdateCoordinator.cs',
    'src/DynamicIsland.App/Services/VelopackUpdateBackend.cs',
    'src/DynamicIsland.App/App.xaml.cs',
    'src/DynamicIsland.App/Settings/SettingsViewModel.cs',
    'src/DynamicIsland.App/Settings/SettingsWindow.xaml',
    'src/DynamicIsland.App/Settings/SettingsWindow.xaml.cs',
    'src/DynamicIsland.UI/Resources/Strings.resx',
    'src/DynamicIsland.UI/Resources/Strings.ru.resx',
}

def previous(path):
    return subprocess.check_output(['git', 'show', BASE + ':' + path]).decode().replace('\r\n', '\n')

def current(path):
    return Path(path).read_text(encoding='utf-8-sig').replace('\r\n', '\n')

changed = subprocess.check_output(['git', 'diff', '--name-only', BASE]).decode().splitlines()
for path in changed:
    assert not path.startswith('src/') or path in ALLOWED, 'Unrelated production change: ' + path
for path in ('src/DynamicIsland.UI/Resources/Strings.resx', 'src/DynamicIsland.UI/Resources/Strings.ru.resx'):
    def strings(text):
        return {node.attrib['name']: node.findtext('value') for node in ET.fromstring(text).findall('data')}
    before, after = strings(previous(path)), strings(current(path))
    assert all(after.get(key) == value for key, value in before.items()), 'Existing translation changed: ' + path
    assert all(key.startswith('Update.') for key in after.keys() - before.keys()), 'Unrelated translation added'
path = 'src/DynamicIsland.Modules.Media/Views/MediaTemplates.xaml'
marker = '<!-- Live pill: tiny artwork on the left, real-level bars on the right. -->'
assert previous(path).split(marker)[1] == current(path).split(marker)[1], 'Compact/live media template changed'
path = 'src/DynamicIsland.App/Settings/SettingsViewModel.cs'
before, after = previous(path), current(path)
start = 'internal sealed partial class GeneralSettingsViewModel'
old_end = '/// <summary>About page.</summary>'
new_end = '/// <summary>About page and the user-visible state of the existing application updater.</summary>'
assert before.split(start)[1].split(old_end)[0] == after.split(start)[1].split(new_end)[0], 'General/module settings changed'
marker = '/// <summary>The settings window: sidebar of sections, content on the right.</summary>'
assert before.split(marker)[1] == after.split(marker)[1], 'Settings navigation changed'
path = 'src/DynamicIsland.App/Settings/SettingsWindow.xaml'
marker = '<!-- About. -->'
assert previous(path).split(marker)[0] == current(path).split(marker)[0], 'Unrelated settings page layout changed'
print('PASS: only Music and updater production scope changed; fonts, palette, shape, other modules and existing translations preserved')
