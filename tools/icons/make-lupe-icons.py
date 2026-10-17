# Baut src/Lupe/Assets/LupeIcons.ttf: die von Lupe genutzten Fluent UI System Icons (MIT, Microsoft) auf den
# Codepunkten von Segoe Fluent Icons – so bleiben die Zeichencodes in XAML auf jedem System dieselben.
#
# Aufruf: python make-lupe-icons.py [ordner mit fonttools]
# im Ordner mit FluentSystemIcons-Regular.ttf und FluentSystemIcons-Regular.json (als r.ttf und r.json) aus
# https://github.com/microsoft/fluentui-system-icons/tree/main/fonts. Ergebnis: LupeIcons.ttf im selben Ordner.
import json
import sys

if len(sys.argv) > 1:
    sys.path.insert(0, sys.argv[1])
from fontTools import subset
from fontTools.ttLib import TTFont

# Segoe-Codepunkt -> Fluent-Name (Größe 20, Regular)
SEGOE = {
    0xE70D: 'chevron_down', 0xE70E: 'chevron_up', 0xE710: 'add', 0xE711: 'dismiss', 0xE713: 'settings',
    0xE71A: 'stop', 0xE71B: 'link', 0xE71E: 'target', 0xE721: 'search', 0xE722: 'camera',
    0xE72C: 'arrow_clockwise', 0xE72E: 'lock_closed', 0xE73E: 'checkmark', 0xE73F: 'arrow_minimize',
    0xE74A: 'arrow_up', 0xE74B: 'arrow_down', 0xE74D: 'delete', 0xE74E: 'save', 0xE751: 'arrow_enter_left',
    0xE756: 'window_console', 0xE765: 'keyboard', 0xE767: 'speaker_2', 0xE768: 'play', 0xE77F: 'clipboard_paste',
    0xE783: 'error_circle', 0xE7A8: 'crop', 0xE7BA: 'warning', 0xE7E8: 'power', 0xE7EF: 'building',
    0xE7F4: 'desktop', 0xE81C: 'history', 0xE838: 'folder_open', 0xE8A5: 'document', 0xE8A7: 'open',
    0xE8B9: 'image', 0xE8C8: 'copy', 0xE8D7: 'certificate', 0xE8E5: 'folder_open', 0xE8FD: 'text_bullet_list_ltr',
    0xE916: 'timer', 0xE945: 'flash', 0xE946: 'info', 0xEB9F: 'image_add', 0xED14: 'qr_code',
}

names = json.load(open('r.json', encoding='utf-8'))
font = TTFont('r.ttf')
cmap = font.getBestCmap()
want = {cp: cmap[names[f'ic_fluent_{name}_20_regular']] for cp, name in SEGOE.items()}

options = subset.Options()
options.name_IDs = ['*']
options.notdef_outline = True
options.layout_features = []
subsetter = subset.Subsetter(options)
subsetter.populate(glyphs=sorted(set(want.values())))
subsetter.subset(font)

for table in font['cmap'].tables:
    if table.isUnicode():
        table.cmap = dict(want)
font['cmap'].tables = [t for t in font['cmap'].tables if t.isUnicode()]
for record in font['name'].names:
    if record.nameID in (1, 4, 16):
        record.string = 'Lupe Icons'
    elif record.nameID == 6:
        record.string = 'LupeIcons'

# Wie Segoe Fluent Icons das ganze Geviert füllen (die Fluent-Symbole lassen 10 % Rand): Geviert 500 -> 400 Einheiten
# (also 1,25-mal so groß) und die Umrisse um den Rand nach links unten verschieben, ohne die Kurven anzufassen
glyf = font['glyf']
old = font['head'].unitsPerEm
margin = old // 10
em = old - 2 * margin
for name in font.getGlyphOrder():
    glyph = glyf[name]
    if glyph.numberOfContours > 0:
        glyph.coordinates.translate((-margin, -margin))
for name in font.getGlyphOrder():
    glyph = glyf[name]
    glyph.recalcBounds(glyf)
    font['hmtx'][name] = (em, getattr(glyph, 'xMin', 0))
font['head'].unitsPerEm = em
font['hhea'].ascent = em
font['hhea'].descent = 0
font['hhea'].lineGap = 0
font['hhea'].advanceWidthMax = em
os2 = font['OS/2']
os2.sTypoAscender = em
os2.sTypoDescender = 0
os2.sTypoLineGap = 0
os2.usWinAscent = em
os2.usWinDescent = 0
font.save('LupeIcons.ttf')
print(f'LupeIcons.ttf: {len(set(want.values()))} Symbole, Geviert {em}')
