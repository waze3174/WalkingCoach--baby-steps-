# WalkingCoach (baby steps)

a melonloader mod for baby steps that shows what nate's legs are feeling, so you can learn to walk instead of guessing. it doesn't make anything easier, it just shows you what's going on.

## what it does

on the ground:
- a square under each planted foot, coloured by grip (green good, yellow so-so, orange bad, light blue ice). it flashes red when that foot slips
- a line between your planted feet. it goes from white to yellow to red as your feet spread towards the splits
- a faint line from your planted foot to the lifted one, so you see a split coming before you put the foot down
- a diamond for where your weight is. green when you're fine, fading to red the further your weight is outside your feet, the more you lean or the closer you are to the splits. solid red when the game is about to drop you

in the bottom left:
- **FEET**: each foot's surface, slope, grip and fatigue, plus SLIPPING when it slides
- **BALANCE**: your lean against the game's limit, and how close you are to the splits
- stays up while you're down

**F4** shows / hides it (on by default).

the panel sits where the game's input display goes, so while the coach shows the input display moves to the bottom middle of the screen (back to its corner when the coach hides). with rulebook's rules list on, the list moves up out of the panel's way.

## how the warnings work

the numbers come from the game's own rules, measured from the game:
- you stay up as long as one planted leg leans less than 55°. past that, you're going over
- both feet planted more than 1.95 leg lengths apart is the splits, instant fall
- grip is the surface's grip, 1.5x on flat ground fading to 1x by 30° of slope

## install

1. install [melonloader](https://melonwiki.xyz/) 0.7.x for baby steps
2. drop `WalkingCoach.dll` in `Baby Steps/Mods/`

## settings

`Baby Steps/UserData/WalkingCoach/WalkingCoach.cfg` (made on first launch):

- `Shown` on or off
- `ToggleKey` the key (default `F4`)

## build

needs the .net 6 sdk and the game with melonloader installed (for the references).

```
dotnet build src/WalkingCoach.csproj -c Release -p:GameDir="C:\path\to\Baby Steps"
```

## credits

baby steps by gabe cuzzillo, maxi boch and bennett foddy.

tested on baby steps (steam) with melonloader 0.7.3, on steam deck.

not affiliated with or endorsed by gabe cuzzillo, maxi boch, bennett foddy or devolver digital.

## licence

cc0 1.0, do whatever you want with it. see `LICENSE`.
