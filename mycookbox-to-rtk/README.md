# mycookbox-to-rtk

Converts a backup from the (abandoned) **My CookBox** Android app into an `.rtk` file that
**My Recipe Box : RecetteTek** can restore/import.

Python 3 standard library only.

```sh
# everything
python3 mcb2rtk.py MCB_Backup.bck MyCookBox_all.rtk

# a few recipes to test an import first (numbers from recipes/recipe_N.rcp in the backup)
python3 mcb2rtk.py MCB_Backup.bck test.rtk --only 1,2,3

# only the recipes that were renamed because their titles clashed
python3 mcb2rtk.py MCB_Backup.bck fix.rtk --renamed-only
```

## Formats

**Input (`.bck`)**: a zip containing `manifest.xml`, `recipeCategories.xml`, `pantry.xml`
and `recipes/recipe_N.rcp`. Each `.rcp` is itself a zip with `recipe.xml` and an optional
`image.jpg`.

**Output (`.rtk`)**: a zip with `recipes_0.json` (max 400 recipes per file),
`categories.json`, `tags.json` and the pictures as `[uuid].jpg`.
Spec: <https://github.com/cmonapp/rtk-format>

## Field mapping

| My CookBox | RecetteTek |
|---|---|
| title | `title` |
| recipeinfo/blurb | `description` |
| recipeinfo/yield | `quantity` |
| recipeinfo/genre | `categories` |
| isFavorite, recipeinfo/rating | `favorite`, `rating` |
| ingredient groups (quantity + fooditem) | `ingredients`, one per line; group names become `Name:` headers |
| preparation | `instructions` |
| recipeinfo/source, non-numeric prep/cook times | `notes` |
| recipeinfo/preptime, cookingtime (when parseable) | `preparationTime`, `cookingTime` (minutes), `totalTime` |
| image.jpg | `pictures` |

## Gotchas

- **The app merges recipes that share a title on import.** Duplicate titles are renamed
  using the "From ..." description (e.g. `Granola (Grandma)`), falling back to the yield
  or `version N`. Exact copies are dropped.
- Group names longer than 40 characters, or groups with no real ingredients, were really
  method steps; they are moved into numbered `instructions` and the ingredient section is
  labelled `Step N:`.
- Placeholder ingredient lines (`0`, emoji only) are dropped, and `2 cups sugar Sugar`
  becomes `2 cups sugar`.
- Recipe UUIDs are deterministic (uuid5 of the entry name and title), so re-running gives
  the same IDs.
