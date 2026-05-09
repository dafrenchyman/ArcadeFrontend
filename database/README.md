# Emu Database creator

This code is currently a mess. It needs a lot of cleanup.

## Setup for development:

- Setup Sqlite `sudo apt install sqlite3 libsqlite3-dev`
- Setup a python 3.x venv (usually in `.venv`)
- `pip3 install --upgrade pip`
- Install pip-tools `pip3 install pip-tools`
- Update dev requirements: `pip-compile --output-file=requirements.dev.txt requirements.dev.in`
- Update requirements: `pip-compile --output-file=requirements.txt requirements.in`
- Install dev requirements `pip3 install -r requirements.dev.txt`
- Install requirements `pip3 install -r requirements.txt`
- `pre-commit install`

## Environment files to set

```
IGDB_CLIENT_ID="..."
IGDB_API_KEY="..."
STEAM_GRID_DB_API_KEY="..."
```

You'll need to get these from their respective sources. They're free.

## To get dat files:

Datomatic files are used to check the hashes of ROMs and get clean names for them.

- Go to the daily page: https://datomatic.no-intro.org/index.php?page=download&s=64&op=daily
- Checkmark all the stuff you want and download
- Currently, everything is hard coded

## To get arcade info files:

- Go to http://adb.arcadeitalia.net/lista_mame.php and then click options and then download the `Detailed CSV`. Don't filter on anything so you can grab everything.
- Extract the file and name it `./database/arcade.csv`

## Convert GamesDB sqldump to sqlite3

- Run the `setup.sh` script in the root. it **should** take care of it.
