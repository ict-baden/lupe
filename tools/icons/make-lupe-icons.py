# Baut src/Lupe/Assets/LupeIcons.ttf: die von Lupe genutzten Fluent UI System Icons (MIT, Microsoft) auf den
# Codepunkten von Segoe Fluent Icons. Aufruf: python sub.py <ordner mit fonttools>, im Ordner mit
# FluentSystemIcons-Regular.ttf/.json (als r.ttf/r.json) aus github.com/microsoft/fluentui-system-icons.
import json, sys
sys.path.insert(0, sys.argv[1])
from fontTools.ttLib import TTFont
from fontTools import subset
m = json.load(open('r.json'))
segoe = {0xE70D:'chevron_down',0xE70E:'chevron_up',0xE710:'add',0xE711:'dismiss',0xE713:'settings',0xE71A:'stop',0xE71B:'link',0xE71E:'target',
0xE721:'search',0xE722:'camera',0xE72C:'arrow_clockwise',0xE72E:'lock_closed',0xE73E:'checkmark',0xE73F:'arrow_minimize',0xE74A:'arrow_up',0xE74B:'arrow_down',
0xE74D:'delete',0xE74E:'save',0xE751:'arrow_enter_left',0xE756:'window_console',0xE765:'keyboard',0xE767:'speaker_2',0xE768:'play',0xE77F:'clipboard_paste',
0xE783:'error_circle',0xE7A8:'crop',0xE7BA:'warning',0xE7E8:'power',0xE7EF:'building',0xE7F4:'desktop',0xE81C:'history',0xE838:'folder_open',0xE8A5:'document',
0xE8A7:'open',0xE8B9:'image',0xE8C8:'copy',0xE8D7:'certificate',0xE8E5:'folder_open',0xE8FD:'text_bullet_list_ltr',0xE916:'timer',0xE945:'flash',0xE946:'info',
0xEB9F:'image_add',0xED14:'qr_code'}
f = TTFont('r.ttf')
cmap = f.getBestCmap()
want = {}
for cp, n in segoe.items():
    want[cp] = cmap[m[f'ic_fluent_{n}_20_regular']]
glyphs = sorted(set(want.values()))
opts = subset.Options(); opts.name_IDs=['*']; opts.notdef_outline=True; opts.layout_features=[]
s = subset.Subsetter(opts); s.populate(glyphs=glyphs); s.subset(f)
for t in f['cmap'].tables:
    if t.isUnicode(): t.cmap = dict(want)
f['cmap'].tables = [t for t in f['cmap'].tables if t.isUnicode()]
for rec in f['name'].names:
    if rec.nameID in (1,4,16): rec.string = 'Lupe Icons'
    elif rec.nameID == 6: rec.string = 'LupeIcons'
f.save('LupeIcons.ttf')
print('ok', len(glyphs))

# Wie Segoe Fluent Icons das ganze Geviert füllen (Fluent-Symbole lassen 10 % Rand): um die Mitte um 1,25 vergrößern
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.pens.transformPen import TransformPen
f = TTFont('LupeIcons.ttf')
glyf = f['glyf']; gs = f.getGlyphSet()
s = 1.25; c = f['head'].unitsPerEm / 2
for name in f.getGlyphOrder():
    if name == '.notdef': continue
    pen = TTGlyphPen(gs)
    gs[name].draw(TransformPen(pen, (s, 0, 0, s, c - s * c, c - s * c)))
    glyph = pen.glyph()
    glyph.recalcBounds(glyf)
    glyf[name] = glyph
    f['hmtx'][name] = (f['hmtx'][name][0], getattr(glyph, 'xMin', 0))
f.save('LupeIcons.ttf')
print('skaliert')
