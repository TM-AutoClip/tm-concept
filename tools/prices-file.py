"""Writes pricing/prices.json from the app's built-in price table (src/TMConcept.Providers/Catalogs/*.json).

    python tools/prices-file.py [date]

The app reads this file from the repository's main branch at most once a day (schema 1; see PriceCheck in TMConcept.Providers):
it only changes the prices of models the app already knows; local models are never listed. To publish new prices, edit the
catalogs (or this file's values), run the script with the date of the prices, commit and push. The date defaults to the newest
pricedOn of the catalogs; a file older than an app's own table is ignored by that app.
"""
import glob
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIELDS = ['inputUsd', 'outputUsd', 'cacheReadUsd', 'cacheWriteUsd', 'longInputUsd', 'longOutputUsd', 'imageUsd',
          'speechUsdPerMillionCharacters']


def main():
    models = []
    dates = []
    for path in sorted(glob.glob(os.path.join(ROOT, 'src', 'TMConcept.Providers', 'Catalogs', '*.json'))):
        with open(path, encoding='utf-8') as handle:
            catalog = json.load(handle)
        if catalog['provider'] == 'local':
            continue
        dates.append(catalog['pricedOn'])
        for model in catalog['models']:
            entry = {'provider': catalog['provider'], 'id': model['id']}
            for field in FIELDS:
                if field in model:
                    entry[field] = model[field]
            models.append(entry)
    date = sys.argv[1] if len(sys.argv) > 1 else max(dates)
    target = os.path.join(ROOT, 'pricing', 'prices.json')
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, 'w', encoding='utf-8', newline='\n') as handle:
        json.dump({'schema': 1, 'pricedOn': date, 'models': models}, handle, ensure_ascii=False, indent=2)
        handle.write('\n')
    print(f'{target}: {len(models)} models, dated {date}')


if __name__ == '__main__':
    main()
