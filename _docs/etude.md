Prompt — Étude d'architecture : Moteur de campagnes multicanal
Contexte
Je suis architecte logicielle spécialisé et je souhaite concevoir un outil de gestion de campagnes d'envoi multicanal (email, SMS, courrier papier), comparable dans l'esprit à Adobe Campaign, mais avec une architecture ouverte et générique, exposée via API. Stack technique de prédilection : C# .NET Core ; SQL Server ; IIS ; SSRS ; SSIS 

Besoin fonctionnel
L'outil repose sur deux profils utilisateurs distincts :

Profil 1 — Le concepteur de modèles (Designer)

Crée et gère des modèles et sous-modèles de mails et de courriers
Définit les zones dynamiques via un système de substitution clé-valeur (à l'image des string format C#)
Contrôle entièrement la charte graphique : couleurs, mise en page, typographie
Gère des contenus avancés : tableaux dynamiques, listes, liens hypertextes
Dispose d'un assistant HTML/éditeur visuel pour concevoir les modèles
Peut prévoir des zones de saisie libre, remplies par l'utilisateur au moment de l'envoi

Profil 2 — L'utilisateur de campagnes (Opérateur)

Sélectionne un modèle existant et le paramètre pour sa campagne
Choisit un référentiel de données source (ex : référentiel clients, collaborateurs…) sur lequel le modèle sera alimenté
Applique des filtres de ciblage sur ce référentiel (ex : clients entre 20 et 30 ans ayant un contrat actif)
Renseigne les valeurs libres communes à tous les envois de la campagne
Définit un planning d'actions : envoi initial, relances mail, envoi de SMS, avec des délais configurables (ex : envoi J0, relance J+15, SMS J+20)
Suit en temps réel l'état d'avancement de ses campagnes (envois, relances)

Architecture technique envisagée
L'outil se décompose en plusieurs briques :

Un moteur d'envoi générique exposé en API

Permet à n'importe quelle application externe d'envoyer un mail en ciblant un modèle et en fournissant les données à substituer
Exemples d'usage : appel direct depuis un SI tiers, déclenchement automatisé, campagne interne

Un moteur de planification et d'orchestration (workers)

Prend en charge la file d'exécution des envois et des générations PDF
Gère la temporisation, les relances, les enchaînements d'actions multicanal
Associe les données au bon modèle et déclenche l'envoi au moment défini

Un référentiel de modèles et de dictionnaires

Stocke les modèles, sous-modèles, et les dictionnaires clé-valeur
Supporte les modèles mail (HTML avancé) et courrier (PDF avec entête propre au courrier)

Un module de génération PDF

Pour les courriers papier : génère les PDF finaux consolidés
Ces PDF sont transmis à un prestataire tiers pour impression et envoi postal

Un connecteur SMS

Délègue l'envoi SMS à un prestataire externe via API

Un module de gestion des référentiels de données

Architecture extensible : aujourd'hui le référentiel clients, demain collaborateurs ou tout autre entité
Le modèle de données doit être suffisamment souple pour intégrer de nouveaux référentiels sans refonte

Points d'attention architecturaux

Généricité du moteur : il doit pouvoir être appelé indépendamment de l'outil de campagne, par n'importe quel système externe
Extensibilité des référentiels : ne pas coupler le moteur à un référentiel métier spécifique
Séparation des responsabilités : bien isoler conception des modèles / pilotage des campagnes / exécution des envois
Traçabilité et suivi : chaque action (envoi, relance, SMS) doit être suivie et consultable
Sécurité et gouvernance : les deux profils ont des droits et périmètres distincts

Ce que j'attends de toi

Je veux que tu étudies la cohérence de mon idée initiale puis que tu identifie et décris les grandes briques techniques de ce système, leurs responsabilités et leurs interfaces
Cartographie les flux et les relations entre ces briques (qui appelle qui, quelles données circulent)
Propose une approche pour la templétisation : comment gérer de manière générique la substitution clé-valeur, les zones libres, les contenus dynamiques (tableaux, listes), et la distinction mail vs courrier
Analyse les points de variabilité et d'extension : comment rendre le système ouvert à de nouveaux canaux, de nouveaux référentiels, de nouveaux consommateurs de l'API
Identifie les risques et les questions ouvertes à trancher avant de descendre en conception détaillée
Propose des schémas (sous forme textuelle ou Mermaid) pour illustrer l'architecture globale et les principaux flux

Ce prompt est volontairement en mode "dégrossissage" : l'objectif est de structurer la réflexion architecturale, pas encore de produire une conception détaillée.
