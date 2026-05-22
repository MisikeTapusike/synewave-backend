# Synewave Backend 🎵

ASP.NET Core 8 Web API + PostgreSQL + Docker, nasaditeľný na Railway.

---

## Štruktúra projektu

```
synewave-backend/
├── src/
│   └── Synewave.API/
│       ├── Controllers/       # API endpointy
│       ├── Data/              # AppDbContext (EF Core)
│       ├── DTOs/              # Request/Response objekty
│       ├── Models/            # Databázové modely
│       ├── Services/          # Biznis logika
│       ├── Program.cs         # Konfigurácia aplikácie
│       └── appsettings.json   # Nastavenia
├── Dockerfile                 # Pre Railway deploy
├── docker-compose.yml         # Lokálny vývoj
└── railway.toml               # Railway konfigurácia
```

---

## Lokálny vývoj (Docker)

```bash
# 1. Spusti databázu aj API naraz
docker-compose up --build

# API beží na:  http://localhost:8080
# Swagger UI:   http://localhost:8080/swagger
# PostgreSQL:   localhost:5432
```

## Lokálny vývoj (bez Dockeru)

### Požiadavky
- .NET 8 SDK
- PostgreSQL 15+

```bash
# 1. Nainštaluj .NET EF tools
dotnet tool install --global dotnet-ef

# 2. Nastav connection string v appsettings.json

# 3. Vytvor a spusti migrácie
cd src/Synewave.API
dotnet ef migrations add InitialCreate
dotnet ef database update

# 4. Spusti API
dotnet run
```

---

## Deploy na Railway

### Krok 1 — Vytvor projekt na Railway
1. Choď na [railway.app](https://railway.app) → **New Project**
2. Vyber **Deploy from GitHub repo**
3. Pripoj tento repozitár

### Krok 2 — Pridaj PostgreSQL
1. V projekte klikni **+ Add Service** → **Database** → **PostgreSQL**
2. Railway automaticky nastaví `DATABASE_URL` premennú

### Krok 3 — Nastav environment variables
V Railway dashboarde → tvoja služba → **Variables**:

```
JWT_SECRET=tvoj-super-tajny-klic-min-32-znakov-ZMEN-TOTO
```

To je všetko! Railway automaticky:
- Buildne Docker image
- Spustí migrácie (cez `Program.cs`)
- Nasadí API

---

## API Endpointy

### 🔐 Auth
| Method | Endpoint | Popis |
|--------|----------|-------|
| POST | `/api/auth/register` | Registrácia |
| POST | `/api/auth/login` | Prihlásenie |

### 👤 Users
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/users/me` | Môj profil |
| GET | `/api/users/me/stats` | Moje štatistiky |
| GET | `/api/users/search?q=...` | Hľadaj používateľov |

### 👫 Friends
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/friends` | Zoznam priateľov s aktivitou |
| GET | `/api/friends/feed` | Feed priateľov |
| POST | `/api/friends/request/{userId}` | Pošli žiadosť |
| PATCH | `/api/friends/request/{id}?action=accept` | Prijmi/odmietni |
| DELETE | `/api/friends/{friendId}` | Odstráň priateľa |
| GET | `/api/friends/{friendId}/compatibility` | % zhoda vkusu |

### 🎵 Tracks
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/tracks/search?q=...` | Hľadaj skladby |
| POST | `/api/tracks` | Pridaj skladbu |
| POST | `/api/tracks/log` | Zaznamenaj prehranie |
| POST | `/api/tracks/{id}/like` | Like/unlike |
| GET | `/api/tracks/liked` | Obľúbené skladby |
| GET | `/api/tracks/history` | História prehrávania |

### 🔗 Collectives
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/collectives` | Moje kolektívy |
| GET | `/api/collectives/{id}` | Detail kolektívu |
| POST | `/api/collectives` | Vytvor kolektív |
| POST | `/api/collectives/{id}/members/{userId}` | Pridaj člena |
| DELETE | `/api/collectives/{id}/leave` | Opusti kolektív |

### 🤖 Recommendations
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/recommendations?count=6` | AI odporúčania |

### 🔔 Notifications
| Method | Endpoint | Popis |
|--------|----------|-------|
| GET | `/api/notifications` | Všetky notifikácie |
| GET | `/api/notifications/unread-count` | Počet neprečítaných |
| PATCH | `/api/notifications/{id}/read` | Označ ako prečítané |
| PATCH | `/api/notifications/read-all` | Označ všetky |

---

## Databázová schéma

```
Users ──────────────────────────────────────────────────
  id, username, email, password_hash, avatar_url,
  spotify_id, created_at, last_active_at

Tracks ─────────────────────────────────────────────────
  id, title, artist, album, cover_url,
  spotify_id, duration_seconds, genre

Friendships ────────────────────────────────────────────
  id, requester_id → Users, addressee_id → Users,
  status (pending/accepted/rejected/blocked)

ListeningHistory ───────────────────────────────────────
  id, user_id → Users, track_id → Tracks,
  played_at, seconds_listened, source

Collectives ────────────────────────────────────────────
  id, name, description, emoji, color, created_by_user_id

CollectiveMembers ──────────────────────────────────────
  id, collective_id → Collectives, user_id → Users, role

Likes ──────────────────────────────────────────────────
  id, user_id → Users, track_id → Tracks, liked_at

Notifications ──────────────────────────────────────────
  id, user_id → Users, type, message, is_read, created_at
```
