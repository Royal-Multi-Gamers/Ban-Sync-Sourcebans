# Application de Synchronisation de Bans depuis un fichier TXT vers Sourcebans et synchronise le Sourcebans vers un fichier TXT

## Description

Cette application synchronise les identifiants Steam (SteamID) d'une base de donn�es MySQL (Sourcebans++) vers un fichier de sortie txt. Elle surveille �galement les modifications du fichier de sortie et met � jour la base de donn�es en cons�quence. L'application utilise l'API Steam pour convertir les SteamID64 en SteamID2 et r�cup�rer les noms des joueurs.

## Fonctionnalit�s

- Synchronisation initiale de la base de donn�es vers le fichier de sortie.
- Surveillance des modifications du fichier de sortie et mise � jour de la base de donn�es.
- Conversion des SteamID64 en SteamID2.
- R�cup�ration des noms des joueurs via l'API Steam.
- Journalisation des �v�nements et des erreurs avec NLog.

## Configuration

### Fichier de Configuration

L'application utilise un fichier `config.json` pour la configuration. Si ce fichier n'existe pas, un fichier de configuration par d�faut sera cr��. Voici un exemple de `config.json` :
```
{
  "ConnectionString": {
    "Server": "localhost",
    "Uid": "databaseuser",
    "Pwd": "userpassword",
    "Database": "databasename"
  },
  "OutputFile": "C:\\testps\\Blacklist.txt",
  "SteamAPIKey": "steamapikey",
  "ServerID": 5,
  "DebugMode": true
}
```


### Variables de Configuration

- `ConnectionString`: Informations de connexion � la base de donn�es MySQL.
  - `Server`: Adresse du serveur MySQL.
  - `Uid`: Nom d'utilisateur de la base de donn�es.
  - `Pwd`: Mot de passe de l'utilisateur de la base de donn�es.
  - `Database`: Nom de la base de donn�es.
- `OutputFile`: Chemin du fichier de sortie � surveiller.
- `SteamAPIKey`: Cl� API Steam pour acc�der aux informations des joueurs.
- `ServerID`: Identifiant du serveur.
- `DebugMode`: Mode de d�bogage (true/false).

## D�ploiement

L'application utilise GitHub Actions pour le d�ploiement. Le workflow de d�ploiement se trouve dans `.github/workflows/release.yml`. Voici les �tapes principales :

1. R�cup�ration du d�p�t.
2. Configuration de .NET.
3. Restauration des d�pendances.
4. Compilation de l'application.
5. Ex�cution des tests.
6. Publication de l'application.
7. Archivage des artefacts.
8. Cr�ation d'une release GitHub.
9. T�l�chargement des artefacts de la release.

## Ex�cution

Pour ex�cuter l'application, utilisez la commande suivante :

```
dotnet run
```

Assurez-vous que le fichier `config.json` est correctement configur� avant de lancer l'application.

## Journalisation

L'application utilise NLog pour la journalisation. Le fichier de configuration NLog (`NLog.config`) doit �tre pr�sent dans le r�pertoire de l'application. Les journaux sont enregistr�s dans le r�pertoire `logs`.

## Contribuer

Les contributions sont les bienvenues ! Veuillez soumettre des pull requests et signaler les probl�mes via GitHub.

## Licence

Ce projet est open source et disponible sous licence MIT.