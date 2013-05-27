MSL.Scraper
=======
A C# console application that scrapes [Raw Images](http://mars.jpl.nasa.gov/msl/multimedia/raw/) from the Mars Science Laboratory (aka [Curiosity Rover](https://twitter.com/MarsCuriosity)) website and creates time lapse MPEG movies from the data; as seen on [YouTube - Mars Science Laboratory: Time Lapse - Sol 0 - Sol 281](http://youtu.be/3FH6QPAD-BU).

Project Structure
-----------------
All of the main program logic is contained in the 'Program.cs' file within the project.

After cloning and opening the 'MSL.Scraper.sln' file, the project will have the following directories:

```
Lib
SQL
```

The `Lib` directory contains any depdendencies not handled by NuGet. This is necessary for the AForge libraries and dependencies.

The `SQL` directory contains two SQL files:
'MSLScraper Stats.sql' - A useful query to get statistics from your scraping database.
'MSLScraper Create Database.edmx.sql' - SQL to create all necessary database objects to run the program.


Building
--------
Just clone the repository, open the solution, and build. Building should take care of fetching all the dependencies.

The Database
------------
MSL.Scraper assumes a SQLExpress database named MSLScraper. There is a file in the 'SQL' folder of the project called 'MSLScraper Create Database.edmx.sql'.  Running this script against your SQLExpress instance will create all the necessary database objects.  If necessary, you can change your connection string in the 'App.config' file for the project.

Video
-----
By default, any MPEG created by this program will be saved to a new directory on your local filesystem under 'C:\MSLScraper\'.  This can be changed in the 'MSL.Scraper.MslCamConstants.SaveBaseDirectory' member.