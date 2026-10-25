import plistlib, sys, json

d = plistlib.load(open(sys.argv[1], 'rb'))
acts = d['WFWorkflowActions']

uuid_names = {}
for a in acts:
    p = a['WFWorkflowActionParameters']
    u = p.get('UUID')
    cn = p.get('CustomOutputName')
    if u:
        uuid_names[u] = (cn, a['WFWorkflowActionIdentifier'].split('.')[-1])


def var(v):
    t = v.get('Type')
    if t == 'ActionOutput':
        n = v.get('OutputName') or uuid_names.get(v.get('OutputUUID'), (None, '?'))[0] or uuid_names.get(v.get('OutputUUID'), (None, '?'))[1]
        s = '{' + str(n) + '}'
    elif t == 'Variable':
        s = '{' + v.get('VariableName', '?') + '}'
    elif t == 'ExtensionInput':
        s = '{捷徑輸入}'
    elif t == 'Clipboard':
        s = '{剪貼簿}'
    elif t == 'CurrentDate':
        s = '{現在日期}'
    elif t == 'Ask':
        s = '{每次詢問}'
    else:
        s = '{' + str(t) + '}'
    agg = v.get('Aggrandizements')
    if agg:
        s = s[:-1] + ' ' + ' '.join(
            (g.get('PropertyName') or g.get('DictionaryKey') or g.get('Type', '')) if isinstance(g, dict) else str(g) for g in agg) + '}'
    return s


def val(v):
    if isinstance(v, dict):
        if 'WFSerializationType' in v:
            st = v['WFSerializationType']
            x = v.get('Value')
            if st == 'WFTextTokenString':
                s = x['string']
                att = x.get('attachmentsByRange', {})
                out = ''
                i = 0
                # replace U+FFFC placeholders in order of range
                ranges = sorted(att.items(), key=lambda kv: int(kv[0].strip('{}').split(',')[0]))
                parts = []
                last = 0
                for k, a in ranges:
                    pos = int(k.strip('{}').split(',')[0])
                    parts.append(s[last:pos])
                    parts.append(var(a))
                    last = pos + 1
                parts.append(s[last:])
                return '"' + ''.join(parts) + '"'
            if st == 'WFTextTokenAttachment':
                return var(x)
            if st == 'WFDictionaryFieldValue':
                items = x.get('WFDictionaryFieldValueItems', [])
                return '{' + ', '.join(val(i.get('WFKey')) + ': ' + val(i.get('WFValue')) for i in items) + '}'
            if st == 'WFContentPredicateTableTemplate':
                return json.dumps(x, ensure_ascii=False, default=str)
            return st + ':' + json.dumps(x, ensure_ascii=False, default=str)
        return json.dumps(v, ensure_ascii=False, default=lambda o: '<bytes %d>' % len(o))
    if isinstance(v, bytes):
        return '<bytes %d>' % len(v)
    if isinstance(v, str):
        return '"' + v + '"'
    return str(v)


skip = {'UUID', 'GroupingIdentifier', 'ShowHeader'}
depth = 0
for n, a in enumerate(acts, 1):
    ident = a['WFWorkflowActionIdentifier'].replace('is.workflow.actions.', '')
    p = a['WFWorkflowActionParameters']
    mode = p.get('WFControlFlowMode')
    if mode in (1, 2) and depth > 0:
        d0 = depth - 1
    elif mode == 2 and depth > 0:
        d0 = depth - 1
    else:
        d0 = depth
    if mode == 2:
        depth = max(depth - 1, 0)
        d0 = depth
    elif mode == 1:
        d0 = depth - 1
    ind = '  ' * d0
    items = []
    for k, v in p.items():
        if k in skip or k == 'WFControlFlowMode':
            continue
        items.append(k + '=' + val(v))
    extra = ''
    if p.get('CustomOutputName'):
        extra = ' → ' + p['CustomOutputName']
    print('%2d %s%s [%s] %s%s' % (n, ind, ident, {0: 'start', 1: 'mid', 2: 'end', None: ''}.get(mode, mode), '; '.join(items), extra))
    if mode == 0:
        depth += 1
