# Guide de Migration - BBR-Ban-Sync v0.0.4 → v0.0.5

Ce guide vous aidera à migrer de l'ancienne version (v0.0.4) vers la nouvelle version optimisée (v0.0.5).

## 🔄 Changements Majeurs

### Configuration
- **Ancien** : `config.json`
- **Nouveau** : `appsettings.json`

### Architecture
- **Ancien** : Code monolithique dans `Program.cs`
- **Nouveau** : Architecture modulaire avec services séparés

### Dépendances
- **Ajoutées** : Microsoft.Extensions.*, NLog.Extensions.Hosting, System.Text.Json
- **Supprimées** : MySql.Data (remplacé par MySqlConnector uniquement)

## 📋 Étapes de Migration

### 1. Sauvegarde de l'Ancienne Configuration

Avant de commencer, sauvegardez votre fichier `config.json` existant :

```bash
cp config.json config.json.backup
```

### 2. Conversion de la Configuration

Utilisez ce script PowerShell pour convertir automatiquement votre `config.json` vers `appsettings.json` :

```powershell
# Script de conversion config.json -> appsettings.json
$oldConfig = Get-Content "config.json" | ConvertFrom-Json

$newConfig = @{
    "ConnectionStrings" = @{
        "DefaultConnection" = "Server=$($oldConfig.ConnectionString.Server);Database=$($oldConfig.ConnectionString.Database);Uid=$($oldConfig.ConnectionString.Uid);Pwd=$($oldConfig.ConnectionString.Pwd);SslMode=Required;"
    }
    "BanSync" = @{
        "OutputFile" = $oldConfig.OutputFile
        "SteamAPIKey" = $oldConfig.SteamAPIKey
        "ServerID" = $oldConfig.ServerID
        "DebugMode" = $oldConfig.DebugMode
        "SyncIntervalMinutes" = 1
        "ReleaseCheckIntervalHours" = 1
        "FileWatcherEnabled" = $true
        "MaxRetryAttempts" = 3
        "RetryDelaySeconds" = 5
        "CacheExpirationMinutes" = 30
    }
    "Discord" = @{
        "Enabled" = $oldConfig.DiscordWebhook.Enabled
        "WebhookUrls" = $oldConfig.DiscordWebhook.Urls
        "EmbedColor" = 16711680
        "ReclamationUrl" = "https://www.clan-rmg.com/playerpanel/"
    }
    "GitHub" = @{
        "Owner" = "Royal-Multi-Gamers"
        "Repository" = "Ban-Sync-Sourcebans"
        "CurrentVersion" = "v0.0.5"
    }
}

$newConfig | ConvertTo-Json -Depth 3 | Out-File "appsettings.json" -Encoding UTF8
Write-Host "Configuration convertie avec succès vers appsettings.json"
```

### 3. Conversion Manuelle (Alternative)

Si vous préférez convertir manuellement, voici la correspondance :

#### Ancien config.json
```json
{
  "ConnectionString": {
    "Server": "localhost",
    "Uid": "user",
    "Pwd": "password",
    "Database": "sourcebans"
  },
  "OutputFile": "C:\\path\\to\\Blacklist.txt",
  "SteamAPIKey": "your-api-key",
  "ServerID": 5,
  "DebugMode": true,
  "DiscordWebhook": {
    "Enabled": true,
    "Urls": ["https://discord.com/api/webhooks/..."]
  }
}
```

#### Nouveau appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=sourcebans;Uid=user;Pwd=password;SslMode=Required;"
  },
  "BanSync": {
    "OutputFile": "C:\\path\\to\\Blacklist.txt",
    "SteamAPIKey": "your-api-key",
    "ServerID": 5,
    "DebugMode": true,
    "SyncIntervalMinutes": 1,
    "ReleaseCheckIntervalHours": 1,
    "FileWatcherEnabled": true,
    "MaxRetryAttempts": 3,
    "RetryDelaySeconds": 5,
    "CacheExpirationMinutes": 30
  },
  "Discord": {
    "Enabled": true,
    "WebhookUrls": ["https://discord.com/api/webhooks/..."],
    "EmbedColor": 16711680,
    "ReclamationUrl": "https://www.clan-rmg.com/playerpanel/"
  },
  "GitHub": {
    "Owner": "Royal-Multi-Gamers",
    "Repository": "Ban-Sync-Sourcebans",
    "CurrentVersion": "v0.0.5"
  }
}
```

### 4. Mise à Jour des Scripts de Déploiement

Si vous utilisez des scripts pour déployer l'application, mettez-les à jour :

#### Ancien
```bash
dotnet BBR-Ban-Sync.dll
```

#### Nouveau (identique)
```bash
dotnet BBR-Ban-Sync.dll
```

### 5. Variables d'Environnement (Optionnel)

La nouvelle version supporte les variables d'environnement pour les configurations sensibles :

```bash
# Exemple pour Docker ou systemd
export ConnectionStrings__DefaultConnection="Server=localhost;Database=sourcebans;Uid=user;Pwd=password;"
export BanSync__SteamAPIKey="your-steam-api-key"
export Discord__WebhookUrls__0="https://discord.com/api/webhooks/..."
```

## 🆕 Nouvelles Fonctionnalités Disponibles

### Configuration Avancée

```json
{
  "BanSync": {
    "SyncIntervalMinutes": 1,           // Intervalle de sync (1-60 min)
    "ReleaseCheckIntervalHours": 1,     // Vérification MAJ (1-24h)
    "FileWatcherEnabled": true,         // Surveillance fichier
    "MaxRetryAttempts": 3,              // Tentatives de retry
    "RetryDelaySeconds": 5,             // Délai entre tentatives
    "CacheExpirationMinutes": 30        // Expiration cache Steam
  }
}
```

### Logging Amélioré

Le nouveau système de logging offre plus de détails et de contrôle. Vous pouvez ajuster le niveau dans `NLog.config` :

```xml
<rules>
  <logger name="*" minlevel="Info" writeTo="logfile,console" />
  <!-- Pour plus de détails, utilisez "Debug" -->
</rules>
```

## 🧪 Test de la Migration

### 1. Test de Configuration

Lancez l'application en mode debug pour vérifier la configuration :

```json
{
  "BanSync": {
    "DebugMode": true
  }
}
```

### 2. Vérification des Fonctionnalités

1. **Base de données** : Vérifiez que la connexion fonctionne
2. **Fichier de sortie** : Confirmez que le fichier est lu/écrit correctement
3. **Discord** : Testez les notifications (si activées)
4. **Steam API** : Vérifiez la récupération des noms de joueurs

### 3. Surveillance des Logs

Surveillez les logs dans le dossier `logs/` pour détecter d'éventuels problèmes :

```bash
tail -f logs/$(date +%Y-%m-%d).log
```

## 🔧 Dépannage

### Problèmes Courants

#### "Configuration section 'BanSync' not found"
- Vérifiez que `appsettings.json` est présent
- Assurez-vous que la structure JSON est correcte

#### "Database connection failed"
- Vérifiez la chaîne de connexion dans `ConnectionStrings:DefaultConnection`
- Testez la connexion manuellement

#### "Steam API key invalid"
- Vérifiez que `BanSync:SteamAPIKey` est correctement configuré
- Testez la clé API directement

### Rollback (Retour en Arrière)

Si vous rencontrez des problèmes, vous pouvez revenir à l'ancienne version :

1. Arrêtez la nouvelle version
2. Restaurez l'ancienne version et `config.json.backup`
3. Redémarrez avec l'ancienne version

## 📞 Support

Si vous rencontrez des problèmes lors de la migration :

1. Consultez les logs détaillés
2. Vérifiez la [documentation](README.md)
3. Ouvrez une [issue GitHub](https://github.com/Royal-Multi-Gamers/Ban-Sync-Sourcebans/issues)

## ✅ Checklist de Migration

- [ ] Sauvegarde de l'ancienne configuration
- [ ] Conversion vers `appsettings.json`
- [ ] Test de la nouvelle version
- [ ] Vérification des fonctionnalités
- [ ] Surveillance des logs
- [ ] Suppression des anciens fichiers (optionnel)

---

**Note** : Cette migration est rétrocompatible au niveau fonctionnel. Toutes les fonctionnalités existantes sont préservées avec des améliorations de performance et de stabilité.
