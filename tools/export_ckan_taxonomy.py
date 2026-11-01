import os
import sys
import glob
import json
import io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

appdata = os.environ.get('LOCALAPPDATA', '')
repos_dir = os.path.join(appdata, 'CKAN', 'repos')
files = [f for f in os.listdir(repos_dir) if f.startswith('4A85405A') and f.endswith('.json')]
if not files:
    print('No CKAN repo json found!')
    sys.exit(1)

repo_path = os.path.join(repos_dir, files[0])
print(f'Loading repo from: {repo_path}')

with open(repo_path, 'r', encoding='utf-8') as f:
    data = json.load(f)

avail = data.get('available_modules', {})

CATEGORIES = {
    'visuals_audio': {
        'id': 'visuals_audio',
        'name_zh': '视觉与音效美化',
        'name_en': 'Visuals & Audio',
        'icon': '🎨',
        'description': '画质升级、光影大气、地表置换、排气羽流、座舱视角、环境音效与太空服材质',
        'official_tags': ['graphics', 'sound', 'suits', 'first-person'],
        'keywords': [
            'texture', 'scatterer', 'visual', 'waterfall', 'plume', 'parallax', 'sound', 'audio', 
            'music', 'reshade', 'skybox', 'lighting', 'eve', 'clouds', 'iva', 'interior', 
            'shader', 'recolor', 'enhancement', 'visuals', 'scenery', 'sunset', 'sunflare',
            'atmosphere visual', 'graphical', 'graphics', 'chatterer', 'soundtrack', 'voice',
            'ambient', 'radio chatter', 'suit', 'spacesuit', 'helmet', 'cockpit view'
        ]
    },
    'tools_utilities': {
        'id': 'tools_utilities',
        'name_zh': '实用工具与信息辅助',
        'name_en': 'Tools & Utilities',
        'icon': '🛠️',
        'description': '飞行数据HUD、自动驾驶、装配增强、弹道预估、便捷操作与系统稳定性修复',
        'official_tags': ['convenience', 'information', 'control', 'editor', 'app'],
        'keywords': [
            'tool', 'helper', 'engineer', 'autopilot', 'hud', 'calculator', 'alarm', 'window', 
            'trajectory', 'editor', 'warp', 'transfer', 'camera', 'docking aid', 'shortcuts', 
            'fix', 'navball', 'display', 'telemetry', 'mechjeb', 'toolbar', 'action group', 
            'altimeter', 'gauge', 'hotkey', 'shortcut', 'quickstart', 'flight indicator',
            'flight data', 'delta-v', 'delta v', 'maneuver', 'node', 'gui', 'menu', 'search',
            'sort', 'filter', 'craft manager', 'save manager', 'backup', 'keybind'
        ]
    },
    'parts_hardware': {
        'id': 'parts_hardware',
        'name_zh': '部件扩展与制造',
        'name_en': 'Parts & Hardware',
        'icon': '🚀',
        'description': '火箭发动机、油箱、货舱、结构件、无人探针核心、机械臂与航空载具组件',
        'official_tags': ['parts', 'uncrewed', 'stock-inventory'],
        'keywords': [
            'part', 'engine', 'tank', 'capsule', 'wing', 'booster', 'solar', 'rover', 'propeller', 
            'wheel', 'payload', 'fairing', 'adapter', 'structure', 'aerospace', 'fuselage', 
            'station', 'probe', 'hardware', 'plane', 'aircraft', 'jet', 'rocket', 'cockpit', 
            'decoupler', 'rcs thruster', 'vernor', 'ssto', 'lander', 'cargo', 'freighter', 
            'fuel tank', 'strut', 'ladder', 'docking port', 'gear', 'chute', 'parachute', 
            'heat shield', 'interstage', 'truss', 'service module', 'satellite'
        ]
    },
    'planets_worlds': {
        'id': 'planets_worlds',
        'name_zh': '星系与天体扩展',
        'name_en': 'Planets & Worlds',
        'icon': '🪐',
        'description': '行星包、太阳系重置、系外行星、地表地貌置换与地面静态发射设施',
        'official_tags': ['planet-pack', 'buildings'],
        'keywords': [
            'planet', 'system', 'moon', 'star', 'kopernicus', 'solar system', 'celestial', 
            'orbit', 'terrain', 'surface', 'launchsite', 'space center', 'building', 'structure', 
            'base', 'statue', 'jool', 'kerbol', 'rss', 'opm', 'gas giant', 'exoplanet', 
            'kerbin', 'duna', 'laythe', 'mun', 'minmus', 'galaxy', 'planetary', 'crater', 'biome'
        ]
    },
    'realism_physics': {
        'id': 'realism_physics',
        'name_zh': '真实模拟与物理重构',
        'name_en': 'Realism & Physics',
        'icon': '⚙️',
        'description': '真实气动、N体引力、再入烧蚀、结构刚度、深空通信拟真与推进剂挥发',
        'official_tags': ['physics', 'resources', 'comms'],
        'keywords': [
            'physics', 'aerodynamics', 'far', 'ferram', 'principia', 'gravitation', 'n-body', 
            'heat', 'reentry', 'thermal', 'rigidity', 'joint', 'antenna', 'frequency', 'signal', 
            'resource', 'propellant', 'boiloff', 'realism overhaul', 'realism', 'remotetech', 
            'radiation', 'orbital decay', 'atmospheric drag', 'lift', 'aerodynamic'
        ]
    },
    'career_life_support': {
        'id': 'career_life_support',
        'name_zh': '生存、科研与生涯深度',
        'name_en': 'Career & Life Support',
        'icon': '🧬',
        'description': '生命维持系统、辐射暴露、科技树演进、深空探测科研与经济合同机制',
        'official_tags': ['crewed', 'science', 'tech-tree', 'career', 'agency'],
        'keywords': [
            'life support', 'kerbalism', 'tac', 'oxygen', 'food', 'habitation', 'radiation', 
            'science', 'experiment', 'contract', 'mission', 'tech tree', 'research', 'career', 
            'fund', 'reputation', 'crew', 'scansat', 'contract configurator', 'milestone', 
            'tourist', 'astronaut', 'salary', 'payroll', 'budget', 'training'
        ]
    },
    'combat_military': {
        'id': 'combat_military',
        'name_zh': '武器防务与拓展玩法',
        'name_en': 'Combat & Military',
        'icon': '⚔️',
        'description': '空战对抗、制导导弹、舰载火炮、防空雷达、装甲系统与机构旗帜',
        'official_tags': ['combat', 'flags'],
        'keywords': [
            'weapon', 'missile', 'bomb', 'gun', 'cannon', 'turret', 'radar', 'armor', 
            'bdarmory', 'combat', 'warfare', 'military', 'flag', 'badge', 'emblem', 
            'target', 'bullet', 'bulletproof', 'warhead', 'torpedo'
        ]
    },
    'core_dependencies': {
        'id': 'core_dependencies',
        'name_zh': '底层核心与前置库',
        'name_en': 'Core & Dependencies',
        'icon': '🧱',
        'description': '代码补丁注入、全局配置管理、底层共用库、多部件切换内核与插件框架',
        'official_tags': ['library', 'plugin', 'config'],
        'keywords': [
            'library', 'api', 'framework', 'patch', 'modulemanager', 'module manager', 'core', 
            'dependency', 'loader', 'b9partswitch', 'harmony', 'wrapper', 'plugin', 'backend',
            'common library', 'shared library', 'utilities library'
        ]
    }
}

functional_priority = [
    'combat_military',
    'visuals_audio',
    'planets_worlds',
    'realism_physics',
    'career_life_support',
    'tools_utilities',
    'parts_hardware'
]

classified_mods = {}
category_counts = {k: 0 for k in CATEGORIES}

for ident, mod_entry in avail.items():
    versions = mod_entry.get('module_version', {})
    if not versions:
        continue
    latest_ver = next(iter(versions.values()))
    name = latest_ver.get('name', ident)
    abstract = latest_ver.get('abstract', '')
    desc = latest_ver.get('description', '')
    raw_tags = set(latest_ver.get('tags', []))
    
    text_corpus = f"{ident} {name} {abstract} {desc}".lower()
    
    assigned_cat = None
    confidence = None
    
    for cat_key in functional_priority:
        if raw_tags & set(CATEGORIES[cat_key]['official_tags']):
            assigned_cat = cat_key
            confidence = 'official_tag'
            break
            
    if not assigned_cat:
        best_cat = None
        best_score = 0
        for cat_key in functional_priority:
            score = sum(1 for kw in CATEGORIES[cat_key]['keywords'] if kw in text_corpus)
            if score > best_score:
                best_score = score
                best_cat = cat_key
        
        if best_score >= 1:
            assigned_cat = best_cat
            confidence = 'hybrid_text'
        elif raw_tags & set(CATEGORIES['core_dependencies']['official_tags']):
            assigned_cat = 'core_dependencies'
            confidence = 'official_tag'
        else:
            for cat_key, cat_data in CATEGORIES.items():
                score = sum(1 for kw in cat_data['keywords'] if kw in text_corpus)
                if score > best_score:
                    best_score = score
                    best_cat = cat_key
            if best_cat:
                assigned_cat = best_cat
                confidence = 'keyword_inference'
            else:
                assigned_cat = 'parts_hardware' if any(w in text_corpus for w in ['pack', 'craft', 'vessel', 'mod']) else 'core_dependencies'
                confidence = 'default_fallback'

    category_counts[assigned_cat] += 1
    classified_mods[ident] = {
        'identifier': ident,
        'name': name,
        'category': assigned_cat,
        'category_name_zh': CATEGORIES[assigned_cat]['name_zh'],
        'confidence': confidence,
        'raw_tags': list(raw_tags),
        'abstract': abstract
    }

output_data = {
    'version': '1.0.0',
    'total_mods': len(classified_mods),
    'category_summary': {
        k: {
            'name_zh': CATEGORIES[k]['name_zh'],
            'name_en': CATEGORIES[k]['name_en'],
            'icon': CATEGORIES[k]['icon'],
            'description': CATEGORIES[k]['description'],
            'count': category_counts[k],
            'ratio': round(category_counts[k] / len(classified_mods), 4)
        } for k in CATEGORIES
    },
    'mods': classified_mods
}

os.makedirs('artifacts', exist_ok=True)
os.makedirs('docs/research', exist_ok=True)

with open('artifacts/ckan_mod_taxonomy_integrated.json', 'w', encoding='utf-8') as f:
    json.dump(output_data, f, ensure_ascii=False, indent=2)

with open('docs/research/ckan_mod_taxonomy_integrated.json', 'w', encoding='utf-8') as f:
    json.dump(output_data, f, ensure_ascii=False, indent=2)

print(f'Successfully integrated and exported {len(classified_mods)} mods to:')
print('  - artifacts/ckan_mod_taxonomy_integrated.json')
print('  - docs/research/ckan_mod_taxonomy_integrated.json')
