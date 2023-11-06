# CreateMissing
A utility to create and update missing entries in the Cumulus MX dayfile.txt file from the monthly log files

## About this program
The CreateMissing utility is a command program written in .Net Framework, so it will run on Windows or Linux. Under Linux you will have to use the mono runtime environment to execute the program.

The utility will read your monthly log files and your dayfile.txt (if it exists). It will then compare the data:
* If a day is missing from your dayfile, but present in your monthly logs, it will create a new dayfile record for you.
* If a day has missing data - it may have been created with a old version of Cumulus - then it add those missing bits of data to the existing day record.

What it will not do is update existing data within a day record. This is deliberate, many people edit bad data in their dayfile, but do not amend the corresponding data in the monthly log file. You do not want all your edits being overwritten by bad data again.

### Data Accuracy
The dayfile records created by CreateMissing are only as good as the source data in the monthly log files. The logging interval used will affect the accuracy of the values generated, if the logs have 1 minute intervals, then generally you will get a more accurate result than if they they have 30 minute intervals. This may affect the times of highs and lows even more than the data values.

## Installing
Just copy the two files (CreateMissing.exe, CreateMissing.exe.config) in the release zip file to your Cumulus MX root folder.

## Before you run CreateMissing
CreateMissing has to be told the first date when you expect data to be available. To do this it reads the "Records Began Date" from your Cumulus.ini file.

This is set to the first time you run Cumulus MX on a new installation.

If you have imported old data from another program, or another installation of Cumulus (and you have used the original Cumulus.ini file), then you will have to edit the Cumulus.ini file to set the date to the beginning of your imported data.

**You must edit the Cumulus.ini file with Cumulus MX STOPPED**

The entry in Cumulus.ini can be found in the [Station] section of the file...

```` ini
[Station]
StartDateIso=YYYY-MM-DD
````

**you must retain the same format**.

However, if CreateMissing finds that the first date in your existing dayfile.txt is earlier than the Records Began Date, it will use that date instead.

CreateMissing also uses your Cumulus.ini file to determine things like when your meteorological day starts, and what units you use for your measurements. So make sure you have all this configured correctly in Cumulus MX before importing data into a new install.

## Running CreateMissing
From Windows, start a command prompt and change the path to your Cumulus MX root folder. Then enter the command:

` > CreateMissing.exe`

From Linux, change your command line path to your Cumulus MX root folder, then enter the command:

` > mono CreateMissing.exe`

## Output
If the utility runs successfully (it may well highlight some issues with your monthly files that need fixing), then it will create a new data\dayfile.txt output file.

Your original file will be saved as data\dayfile.txt.sav

If the saved file is already present, CreateMissing will not overwrite it, it will just refuse to process your dayfile again until you rename or save the backup file somewhere else.

In addition to the information output to the console, each run of CreateMissing will create a new log file in your MXdiags folder. You may need to refer to that file when fixing up issues with your monthly log files.

## Cumulus MX
Please note that though you can run CreateMissing without stopping Cumulus MX [1], you must stop/start Cumulus MX if you want it to pickup any new data in your dayfile.txt

[1] It is not safe to run CreateMissing if Cumulus MX is writing to the dayfile. For example it is performing "catch-up" processing at start-up, or end of day processing at the end/start of your meteorological day.
