# CodeRag — opis projektu

## Co to jest

CodeRag to lokalny, offline'owy system RAG (Retrieval-Augmented Generation) dla projektów C#. Pozwala przeszukiwać kod źródłowy **semantycznie** — tzn. nie po nazwie czy słowie kluczowym, ale po znaczeniu i intencji. Zamiast szukać `ValidateOrder`, możesz zapytać „walidacja zamówień przed płatnością" i dostać trafne wyniki.

System indeksuje kod do lokalnej bazy SQLite z osadzonymi wektorami (embeddings), a następnie udostępnia CLI do wyszukiwania. Żadne dane nie opuszczają maszyny — modele ONNX działają lokalnie.

## Do czego może być wykorzystywany

### Praca z LLM (główne zastosowanie)
Claude Code i inne asystenty AI mają ograniczone okno kontekstu. Nie mogą wczytać całego projektu naraz. CodeRag rozwiązuje ten problem: zamiast dawać AI cały kod, AI pyta indeks o konkretne fragmenty — tylko to, co jest potrzebne w danej chwili.

Projekt zawiera gotową integrację jako skill Claude Code (`~/.claude/skills/rag/`). Claude może:
- wyszukiwać kod po znaczeniu (`rag query . -q "obsługa błędów bazy"`)
- znajdować wszystkie miejsca gdzie symbol jest używany przed refaktoryzacją (`-ct SymbolUsage`)
- wąsko filtrować wyniki po klasie, pliku, przestrzeni nazw

### Eksploracja nieznanego projektu
Przy wchodzeniu w nowy, duży codebase można szybko znaleźć „gdzie jest logika X" bez znajomości struktury plików.

### Analiza wpływu zmian (blast radius)
Przed zmianą sygnatury metody lub klasy — zapytaj o `SymbolUsage`, żeby zobaczyć wszystkie miejsca użycia w projekcie.

### Wyszukiwanie po plikach i strukturze
Można wyszukiwać pliki po nazwie (`-fn .csproj`), filtrować po namespace, klasie, typie symbolu.

---

## Jak działa

### Indeksowanie

```
plik .cs
   ↓
CSharpSyntaxExtractor        → chunki Symbol (klasy, metody, właściwości, ...)
                             → chunk FileDocument (cały plik jako jeden chunk)
SymbolReferenceExtractor     → chunki SymbolUsage (gdzie dany symbol jest używany)
   ↓
MiniLmEmbeddingModel (ONNX)  → embedding wektorowy 384-wymiarowy
   ↓
SQLite + vec0                → zapis do bazy
```

Każdy plik `.cs` produkuje trzy kategorie chunków:
- **Symbol** — jeden chunk per deklaracja (klasa, metoda, właściwość, konstruktor, enum, interfejs, record, pole)
- **FileDocument** — jeden chunk z całym plikiem (dla zapytań o pliki, a nie symbole)
- **SymbolUsage** — jeden chunk per używany symbol, zawierający listę linii kodu gdzie ten symbol jest wywoływany/używany

### Baza danych

SQLite z rozszerzeniem `vec0` (wektor similarity search). Schemat:

```
chunks                     — metadane wszystkich chunków
chunk_embeddings_symbol    — wektory dla chunków Symbol
chunk_embeddings_filedocument  — wektory dla FileDocument
chunk_embeddings_symbolusage   — wektory dla SymbolUsage
```

Trzy osobne tabele wektorowe per `ChunkKind` — umożliwia filtrowanie po typie chunka już na poziomie wyszukiwania wektorowego (nie post-filter).

### Przyrostowe indeksowanie

`IndexingPipeline` oblicza hash zawartości każdego chunka (`ContentHash`). Przy reindeksowaniu embeddingi są przeliczane tylko dla chunków, które się zmieniły. Przy usunięciu symbolu z pliku — cały plik jest re-syncowany.

### Wyszukiwanie

Dwa tryby:
1. **Wektorowe** (`SearchAsync`) — gdy podano `-q`. Embedding zapytania porównywany z wektorami w bazie (cosine distance via vec0). Filtry SQL nakładane na wyniki JOIN z tabelą `chunks`.
2. **Filtrowe** (`GetByFiltersAsync`) — gdy brak `-q`, ale są filtry (`-ic`, `-in`, itp.). Zwykłe SQL bez wektora.

Wyniki sortowane po distance (podobieństwo), obcięte do `--results`.

### Modele

- **all-MiniLM-L6-v2** (384 wymiary) — szybki, lekki model do embeddingów kodu i tekstu
- Tokenizer własny (`SimpleTokenizer`) — BPE / WordPiece bez zewnętrznych zależności
- Runtime: Microsoft.ML.OnnxRuntime, opcjonalnie z GPU

### Obserwowanie zmian

`rag watch` używa `FileSystemWatcher` z debouncingiem (`DebouncedQueue`) — zmiany w plikach są zbierane przez chwilę i indeksowane w jednej partii, żeby uniknąć wielokrotnego przeliczania przy szybkich zapisach (np. z IDE).

### Blokowanie

Plik `.rag/index.lock` synchronizuje dostęp między procesami. `rag query` czeka automatycznie, jeśli trwa `rag index` lub `rag watch`. Wewnątrz procesu `SqliteChunkRepository` używa `SemaphoreSlim(1,1)` — SQLite jest single-writer.

---

## Typy chunków

| ChunkKind | Co reprezentuje | SymbolKind |
|-----------|----------------|------------|
| `Symbol` | Deklaracja kodu: klasa, metoda, właściwość, enum, itp. | `Class`, `Record`, `Interface`, `Enum`, `Method`, `Constructor`, `Property`, `Field` |
| `FileDocument` | Cały plik `.cs` jako jeden chunk | — |
| `SymbolUsage` | Zbiór linii gdzie dany symbol jest używany w pliku | — |

---

## Struktura projektu

```
src/
  CodeRag.Core/
    Parsing/          — ekstrakcja chunków z kodu (Roslyn)
    Embedding/        — model ONNX, tokenizer
    Storage/          — SQLite, vec0, repozytorium
    Query/            — QueryOptions, QueryResult, RagQueryService
    Watching/         — FileWatcher, DebouncedQueue, IndexingPipeline
  CodeRag.Cli/
    Commands/         — index, query, watch, status
  CodeRag.FileEdit/   — narzędzie `fe` do edycji linii pliku

tests/
  CodeRag.Core.Tests/ — testy jednostkowe parsera i hasherów
```

---

## Narzędzie fe (File Edit)

Companion CLI do edycji pliku linia po linii. Utrzymuje "aktywny plik" między wywołaniami. Powstał jako uzupełnienie dla Claude Code — kiedy `Edit` zawodzi z powodu nieunikalnego stringa, `fe` pozwala edytować po numerach linii.

```bash
fe -sf src/Foo.cs     # ustaw aktywny plik
fe -rl 10 "..."       # zamień linię 10
fe -il 5 "..."        # wstaw przed linią 5
fe -dl 3              # usuń linię 3
```
