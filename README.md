# Large File Sort

Two C# (.NET 10) console programs for the 3dEYE test assignment:

- **Generator**: creates a test file of a given size.
- **Sorter**: sorts a file of about 100 GB that does not fit in memory.

## Task

Each line of the input has the format `<Number>. <String>`, for example:

```
415. Apple
30432. Something something something
1. Apple
32. Cherry is the best
2. Banana is yellow
```

Lines are sorted by the String part first. Lines with the same String are sorted by Number, ascending:

```
1. Apple
415. Apple
2. Banana is yellow
32. Cherry is the best
30432. Something something something
```

Requirements:

- **Generator**: produces a file in this format, with some lines sharing the same String part, at a size the user chooses.
- **Sorter**: sorts by the criteria above and handles very large files (~100 GB) efficiently.

The evaluation looks at code quality, performance, memory management, test coverage, and multithreading.

## How to run

Requires the .NET 10 SDK.

```powershell
# generate a test file: asks for the size in MB (1 MB = 1,048,576 bytes), writes build/input-<size>MB.txt
dotnet run -c Release --project src/Xakpc.Tasks.LargeFileSort.Generator

# sort it
dotnet run -c Release --project src/Xakpc.Tasks.LargeFileSort -- build/input-1024MB.txt build/output-sorted.txt

# tests
dotnet test

# benchmarks (input files are generated once and cached in %TEMP%\LargeFileSortBenchmarks)
dotnet run -c Release --project bench/Xakpc.Tasks.LargeFileSort.Benchmarks -- --filter '*'
```

The sorter also has launch profiles (`Sort 1GB`, `Sort 10GB`, `Sort 100GB`) that read `build/input-*.txt`, so it can be started from the IDE without typing arguments.

### Disk and memory needs

| Resource | Needed |
|---|---|
| Disk, temp folder | About the input size, for the chunk files. They go to `%TEMP%`; set `TEMP` to use another drive. |
| Disk, output | The input size. |
| RAM | About 7.5 GB peak with the defaults (8 workers, 128 MB chunks), measured on 10 GB and 100 GB inputs, during chunk building. The live data is about `(workers + 1) × chunk size × 3.5` ≈ 4 GB; finished chunks stay on the heap until the next full garbage collection, which roughly doubles it. The merge needs about 1.5 GB. |

For a 100 GB input, plan for ~200 GB of free disk on top of the input file.

## Project layout

```
src/Xakpc.Tasks.LargeFileSort.Generator    test file generator
  Generation/  PhraseGenerator, GenerateResult
src/Xakpc.Tasks.LargeFileSort              sorter
  Chunking/    phase 1: ChunkBuilder (parallel read, sort, write), ChunkWorkspace (temp folder), SortedChunks
  Merging/     phase 2: ChunkMerger (k-way merge into the output)
  Common/      DataRow (parsed line), DataRowComparer, BufferedFile (buffered I/O)
test/Xakpc.Tasks.LargeFileSort.Tests       unit and integration tests (xUnit), same folders as the sorter
bench/Xakpc.Tasks.LargeFileSort.Benchmarks BenchmarkDotNet benchmarks
```

The code is organized by feature: each phase of the sort has its own folder, and only what both phases use lives in `Common/`.

## Design decisions

### Algorithm: external merge sort

The file is bigger than memory, so it is sorted in two phases:

1. **Build chunks:** read the input in chunks that fit in memory, sort each chunk, and write it to a temp file.
2. **Merge:** k-way merge all chunk files into the output with a `PriorityQueue`. Memory is one line per chunk.

### Parallelism: only where it pays off

Measurements of the first, sequential version of the sorter on 1 GB showed where the time goes (see [Benchmarks](#benchmarks)): sorting chunks took 65% of the run, reading and parsing 16%, writing 9%, and merging 10%.

- **Chunk building is parallel.** One reader cuts the input into chunks, and up to N workers parse, sort and write them at the same time.
- **Reading stays sequential.** There is one file read in order, and reading is cheap compared with sorting.
- **The merge stays single-threaded.** It produces one output stream in global order. Splitting it would need range partitioning with byte-offset indexes into every chunk, and that complexity was not worth it here.

The parallel phase uses `Parallel.ForEach` over a lazy `IEnumerable<Chunk>`, wrapped in `Partitioner.Create(..., EnumerablePartitionerOptions.NoBuffering)`:

- **Bounded memory.** By default `Parallel.ForEach` takes items in batches, so one worker could hold several 128 MB chunks. `NoBuffering` gives each worker exactly one chunk at a time, so at most N + 1 chunks are in memory.
- **Sequential reading for free.** The partitioner calls the iterator under a lock, so the reader runs on one thread at a time, in file order.
- **No hand-written plumbing.** Ending the work, capping concurrency and propagating worker failures are built in. A failed worker cannot leave the reader blocked forever.
- **Deterministic output.** Chunk files are named by chunk index, not by completion order, so the result is the same for any number of workers.

The alternative was a `Channel<T>` pipeline with a dedicated reader thread. It gives the same throughput, because in both designs one thread reads at all times and reading is the floor. It needs much more code, though, especially for failure handling.

Workers default to `min(cores, 8)`. On the test machine, going from 8 to 16 workers gave no speedup, only more memory use.

The sequential sorter was removed once the parallel one existed: the parallel sorter with one worker is already faster, so it serves as the baseline in the benchmarks.

### Garbage collection: Server GC

Every input line becomes a short-lived string, and several workers allocate them at the same time. The sorter runs with Server GC (`<ServerGarbageCollection>` in the project file), which gives each core its own heap. It was faster than the default workstation GC: 13–14 s vs 15 s on 1 GB, and 2 min 57 s vs 3 min 11 s on 10 GB. Peak memory was the same in both modes (7.6 vs 7.5 GB on 10 GB).

### I/O: synchronous reads with large buffers

A benchmark of reading 1 GB line by line (`ReadBenchmarks`) showed:

- Synchronous `ReadLine` with a 1 MB buffer is 2.6× faster than `await ReadLineAsync` with the default 4 KB buffer, and allocates 43% less.
- Async I/O brings nothing here: the code reads one line at a time and has no other work to do while it waits.

The sorter therefore uses synchronous reads and writes with 1 MB buffers. Readers in the merge use 64 KB buffers, because one reader per chunk is open at once (~800 chunks for 100 GB).

### Comparison rules

- **String part:** ordinal comparison, by UTF-16 code unit. It is culture-independent, deterministic and fast. So uppercase sorts before lowercase (`Zebra` < `apple`).
- **Number part:** compared as digit strings, without parsing. Leading zeros are ignored, a longer number is bigger, and same-length numbers compare digit by digit. Numbers of any length work, including ones beyond `ulong`.

### Parsing and edge cases

- **Output lines are byte-for-byte input lines.** Each parsed row keeps its original line, so nothing is trimmed or reformatted (`007. a` stays `007. a`).
- **Strict number format.** The Number part must be one or more ASCII digits: no sign, no whitespace, no non-ASCII digits. The separator is the first `". "`, so the String part may itself contain `". "`.
- **Malformed lines are skipped and counted.** The sorter prints the count.
- **The output path must differ from the input path.**
- **Temp files are cleaned up.** Each run uses its own `%TEMP%\LargeFileSort-<guid>` folder and deletes it on completion. The folder holds an exclusive lock file while the run is alive. At startup the sorter removes folders left behind by killed runs, but only those whose lock it can take, so a sort running in another process is never touched.

### Generator

- **Phrases:** 1–3 random words from a 10,000-word English dictionary.
- **Repeated String parts:** about 10% of lines reuse a phrase from a pool of earlier phrases, so the file always has lines with the same String part. Short phrases also repeat naturally.
- **Numbers:** a SplitMix64 bijection of the line index, so they look random and are unique.
- **Reproducible:** the seed is fixed, so the same size produces the same file.
- **Exact sizes:** size is counted in UTF-8 bytes, and the file ends after the line that reaches the requested size.

## Tests

48 tests (xUnit):

| Area | What is covered |
|---|---|
| `DataRow` | Parsing valid lines (text containing `". "`, leading zeros, numbers beyond `ulong`, whitespace kept) and malformed ones (no separator, signs, spaces, non-ASCII digits) |
| `DataRowComparer` | The task example, numeric vs lexicographic order, leading zeros, `ulong.MaxValue + 1`, ordinal case order |
| End-to-end sort | `ExternalSortTests` runs chunking and merging together: the task example, duplicate lines, unusual but valid lines kept unchanged, malformed lines counted, empty file, and a random 2,000-line input compared with an in-memory `OrderBy` |
| `ChunkBuilder` | Chunk boundaries, identical chunk files for 1 and 8 workers, a failing worker throws instead of hanging, missing input file |
| `ChunkWorkspace` | Unique folders, chunk naming, cleanup on dispose, stale-folder cleanup that keeps live folders |
| Generator | Requested size reached, byte counting with non-ASCII words, repeated String parts with unique numbers, same output for the same input |

Large outputs (1 GB, 5 GB, 10 GB and 100 GB) were also checked with a separate script. It confirms that every line is in order, and that the output has exactly the same lines as the input: same line count and the same order-independent hash of all lines.

## Benchmarks

Machine: AMD Ryzen 7 7840U (8 cores / 16 threads), 32 GB RAM, Samsung 990 EVO Plus NVMe SSD, Windows 11, .NET 10.0.12. BenchmarkDotNet in monitoring mode: 1 warmup and 3 measured runs (5 for reads).

Input files come from the generator: 1 GB is ~28.6M lines.

> The laptop's results vary between runs, sometimes by up to 2× (thermal and power state). Comparisons are made within a single benchmark run.

### Sequential vs parallel, 1 GB end to end

All rows come from one benchmark run, with workstation GC (before the switch to Server GC). The sequential sorter has since been removed; see git history.

| Sorter | Workers | Chunk | Mean | vs sequential |
|---|---|---|---|---|
| Sequential | 1 | 512 MB | 37.6 s | 1.0× |
| Parallel | 1 | 128 MB | 28.9 s | 1.3× |
| Parallel | 2 | 128 MB | 19.8 s | 1.9× |
| Parallel | 4 | 128 MB | 14.5 s | 2.6× |
| **Parallel** | **8** | **128 MB** | **12.8 s** | **2.9×** |
| Parallel | 16 | 64 MB | 12.5 s | 3.0× |

Notes:

- **Even 1 parallel worker beats the sequential sorter.** Its chunks are smaller, and several small sorts are cheaper than one large one (n log n, and better cache use).
- **Scaling flattens after 4–8 workers.** About 10 s of the run is the single reader plus the single-threaded merge, and those do not get faster with more workers.

### Larger files

| Input | Sorter | Chunks | Time | Per GB |
|---|---|---|---|---|
| 1 GB | Parallel, 8 workers | 8 | 12.8 s | 12.8 s |
| 5 GB | Parallel, 8 workers | 40 | 1 min 15 s | 15 s |
| **100 GB** | **Parallel, 8 workers, Server GC** | **782** | **34 min 54 s** | **21 s** |
| 5 GB | Sequential (before I/O changes), 64 MB chunks | 80 | ~8 min | ~96 s |

The cost per GB grows with file size, because the single-threaded merge does more work per line with more chunks. The 100 GB run sorted 2,790,092,798 lines. Chunk building took about 10 minutes; the merge took the remaining ~25 minutes at about 65 MB/s, with one core fully busy. Peak memory was 7.7 GB, and the output is exactly as large as the input.

### Where the time went (sequential, 1 GB, 2 chunks)

Measured with stopwatches before parallelizing. Garbage-collection pauses were 1.8 s in total.

| Step | Time | Share |
|---|---|---|
| Read + parse | 5.1 s | 16% |
| **Sort** | **20.7 s** | **65%** |
| Write chunks | 2.9 s | 9% |
| Merge | 3.2 s | 10% |

### Chunk size, sequential sorter, 1 GB

Measured with an earlier version that still used `ReadLineAsync` with default buffers, before the I/O changes below. That is why 512 MB takes 47.4 s here but 37.6 s in the sequential vs parallel table. Compare rows within this table only.

| Chunk | Chunks | Mean |
|---|---|---|
| 64 MB | 16 | 79.8 s |
| 256 MB | 4 | 70.3 s (runs 44–89 s, noisy) |
| **512 MB** | 2 | **47.4 s** |
| 1024 MB | 1 | ~69 s |

Before parallelism, larger chunks meant fewer merge inputs. A single 1 GB chunk was slower again, because of one huge in-memory sort and garbage-collection pressure.

### Reading 1 GB line by line

| Method | 4 KB buffer | 1 MB buffer | Allocated |
|---|---|---|---|
| `ReadLine` (sync) | 1.31 s | **0.85 s** | 2.56 GB |
| `ReadLineAsync` | 2.25 s | 1.22 s | 4.5 GB |
| `ReadLineAsync`, async file handle | 3.80 s | 1.21 s | 4.5 GB |

## What was done with AI

AI (Claude Code) worked as a reviewer and pair programmer. The author reviewed every change, and directed or rejected many of them. The first version of the solution (generator, sequential external sort, first tests) existed before the AI-assisted work started.

**Done with AI assistance:**

- **Review** of the first version against the assignment: found the hardcoded paths, the size-unit mismatch, silently dropped lines, `Trim()` altering lines, `Dispose()` deleting the whole temp root, and missing multithreading.
- **Edge-case fixes:**
  - Keeping original lines.
  - Digit-string number comparison.
  - Strict parsing.
  - Counting skipped lines.
  - Per-run temp folders with lock files.
  - Stale-folder cleanup.
- **Test expansion,** including:
  - The shared contract suite that runs against both sorters.
  - Generator tests.
  - Parallel-specific tests.
- **The benchmark project:**
  - Cached input data.
  - Chunk-size, read-method, and sequential-vs-parallel benchmarks.
- **The external check** used to verify the large output files.
- **Drafting this README.**

**Decisions made or corrected by the author:**

- **Rejected extra complexity:**
  - A command-line interface with optional arguments, cut down to the minimum.
  - A timing-report table, replaced with plain stopwatches. They were removed from the final code once BenchmarkDotNet covered timing.
  - The sequential sorter, with its interface and base class, once the parallel sorter with one worker beat it.
  - Starting multithreaded work before it was asked for.
- **Structure:**
  - Chunk files go to the system temp folder, instead of being passed in by the caller.
  - Folder and file handling was extracted into `ChunkWorkspace`.
  - Feature folders (`Chunking/`, `Merging/`, `Common/`) instead of a `Data` / `Services` split.
- **Process:**
  - Stopped a long benchmark that could not answer the open question.
  - Asked for measurements before optimizing. They showed that sorting was the bottleneck, not reading.

**Mistakes the AI made:**

- **It assumed a requirement** ("both parts can repeat") that is not in the assignment, before the assignment PDF was provided.
- **It overstated the cost of async per-line reads.** The read benchmark showed it was ~6% of total time, not the main cost.
