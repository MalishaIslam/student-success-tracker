using StudentSuccess.Web.Data;
using StudentSuccess.Web.Models;

namespace StudentSuccess.Tests;

/// <summary>
/// In-memory stand-in for SQL Server, loaded from the synthetic sample CSVs.
/// It follows the same rules as the stored procedures, so controllers can be
/// tested without a database.
/// </summary>
public sealed class FakeStudentRepository : IStudentRepository
{
    private sealed record Student(int Id, string Gender, string Region, string Education, string? Imd, string AgeBand, bool Disability);

    private sealed class Enrollment
    {
        public int Id;
        public int StudentId;
        public int PresentationId;
        public int PreviousAttempts;
        public int Credits;
        public int? RegDay;
        public int? UnregDay;
        public string? Result;
        public DateTime UpdatedAt = DateTime.UtcNow;
        public long Version = 1;
    }

    private sealed record Assessment(int Id, int PresentationId, string Type, int? DueDay, decimal Weight);

    private sealed class Result
    {
        public int StudentId;
        public int AssessmentId;
        public int? SubmittedDay;
        public bool IsBanked;
        public decimal? Score;
    }

    private readonly List<PresentationOption> _presentations = [];
    private readonly Dictionary<int, Student> _students = [];
    private readonly List<Enrollment> _enrollments = [];
    private readonly List<Assessment> _assessments = [];
    private readonly List<Result> _results = [];
    private readonly List<AuditEntry> _audit = [];
    private int _nextEnrollmentId = 1;

    public FakeStudentRepository(string? sampleFolder = null)
    {
        var folder = sampleFolder ?? Path.Combine(AppContext.BaseDirectory, "sample-data");

        foreach (var c in Read(folder, "courses.csv"))
        {
            _presentations.Add(new PresentationOption(_presentations.Count + 1, c[0], c[1], ToInt(c[2])));
        }

        foreach (var a in Read(folder, "assessments.csv"))
        {
            _assessments.Add(new Assessment(int.Parse(a[2]), PresentationId(a[0], a[1]), a[3], ToInt(a[4]), decimal.Parse(a[5])));
        }

        var registrations = Read(folder, "studentRegistration.csv")
            .ToDictionary(r => (r[0], r[1], int.Parse(r[2])), r => (Reg: ToInt(r[3]), Unreg: ToInt(r[4])));

        foreach (var s in Read(folder, "studentInfo.csv"))
        {
            var id = int.Parse(s[2]);
            _students.TryAdd(id, new Student(id, s[3], s[4], s[5], s[6] == "?" ? null : s[6], s[7], s[10] == "Y"));
            registrations.TryGetValue((s[0], s[1], id), out var reg);
            _enrollments.Add(new Enrollment
            {
                Id = _nextEnrollmentId++, StudentId = id, PresentationId = PresentationId(s[0], s[1]),
                PreviousAttempts = int.Parse(s[8]), Credits = int.Parse(s[9]),
                RegDay = reg.Reg, UnregDay = reg.Unreg, Result = s[11],
            });
        }

        foreach (var r in Read(folder, "studentAssessment.csv"))
        {
            _results.Add(new Result
            {
                AssessmentId = int.Parse(r[0]), StudentId = int.Parse(r[1]), SubmittedDay = ToInt(r[2]),
                IsBanked = r[3] == "1", Score = r[4] == "?" ? null : decimal.Parse(r[4]),
            });
        }
    }

    public int EnrollmentCount => _enrollments.Count;
    public int ResultCount => _results.Count;
    public int Calls { get; private set; }
    public int FirstEnrollmentId => _enrollments.Min(e => e.Id);

    // ------------------------------------------------------------------ reads

    public Task<Lookups> GetLookupsAsync(CancellationToken ct)
    {
        Calls++;
        var students = _students.Values;
        return Task.FromResult(new Lookups(
            _presentations.OrderBy(p => p.ModuleCode).ThenBy(p => p.PresentationCode).ToList(),
            students.Select(s => s.Region).Distinct().Order().ToList(),
            students.Select(s => s.AgeBand).Distinct().Order().ToList(),
            students.Select(s => s.Education).Distinct().Order().ToList(),
            students.Where(s => s.Imd is not null).Select(s => s.Imd!).Distinct().Order().ToList()));
    }

    public Task<PagedResult<EnrollmentRow>> SearchAsync(EnrollmentSearch s, CancellationToken ct)
    {
        Calls++;
        if (!EnrollmentSearch.SortColumns.Contains(s.Sort))
        {
            throw new InvalidInputException("Unknown sort column.");
        }

        var rows = _enrollments
            .Select(e => (e, st: _students[e.StudentId], p: Presentation(e.PresentationId)))
            .Where(x => s.StudentIdValue is null || x.e.StudentId == s.StudentIdValue)
            .Where(x => s.Module is null || x.p.ModuleCode == s.Module)
            .Where(x => s.Presentation is null || x.p.PresentationCode == s.Presentation)
            .Where(x => s.Region is null || x.st.Region == s.Region)
            .Where(x => s.AgeBand is null || x.st.AgeBand == s.AgeBand)
            .Where(x => s.Result is null || (s.Result == EnrollmentSearch.InProgress ? x.e.Result is null : x.e.Result == s.Result))
            .Select(x =>
            {
                var scores = ResultsFor(x.e).ToList();
                decimal? average = scores.Any(r => r.Score.HasValue) ? Math.Round(scores.Where(r => r.Score.HasValue).Average(r => r.Score!.Value), 1) : null;
                return new EnrollmentRow(x.e.Id, x.e.StudentId, x.p.ModuleCode, x.p.PresentationCode, x.st.Gender,
                    x.st.Region, x.st.AgeBand, x.e.Credits, average, scores.Count, x.e.Result);
            })
            .ToList();

        Func<EnrollmentRow, IComparable?> key = s.Sort switch
        {
            "Course" => r => r.ModuleCode + r.PresentationCode,
            "Region" => r => r.Region,
            "AgeBand" => r => r.AgeBand,
            "Credits" => r => r.StudiedCredits,
            "AvgScore" => r => r.AvgScore,
            "Submitted" => r => r.SubmittedCount,
            "FinalResult" => r => r.FinalResult,
            _ => r => r.StudentId,
        };

        var ordered = (s.IsDescending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ThenBy(r => r.EnrollmentId).ToList();
        var page = ordered.Skip((s.Page - 1) * s.PageSize).Take(s.PageSize).ToList();
        return Task.FromResult(new PagedResult<EnrollmentRow>(page, rows.Count, s.Page, s.PageSize));
    }

    public Task<EnrollmentDetail?> GetEnrollmentAsync(int enrollmentId, CancellationToken ct)
    {
        Calls++;
        var e = _enrollments.FirstOrDefault(x => x.Id == enrollmentId);
        if (e is null)
        {
            return Task.FromResult<EnrollmentDetail?>(null);
        }

        var s = _students[e.StudentId];
        var p = Presentation(e.PresentationId);
        var assessments = _assessments
            .Where(a => a.PresentationId == e.PresentationId)
            .OrderBy(a => a.DueDay is null).ThenBy(a => a.DueDay).ThenBy(a => a.Id)
            .Select(a =>
            {
                var r = _results.FirstOrDefault(x => x.StudentId == e.StudentId && x.AssessmentId == a.Id);
                return new AssessmentRow(a.Id, a.Type, a.DueDay, a.Weight, r?.SubmittedDay, r?.IsBanked ?? false, r?.Score, r is not null);
            })
            .ToList();
        var others = _enrollments
            .Where(x => x.StudentId == e.StudentId && x.Id != e.Id)
            .Select(x => new OtherEnrollment(x.Id, Presentation(x.PresentationId).ModuleCode, Presentation(x.PresentationId).PresentationCode, x.Result))
            .ToList();

        return Task.FromResult<EnrollmentDetail?>(new EnrollmentDetail(
            e.Id, s.Id, s.Gender, s.Region, s.Education, s.Imd, s.AgeBand, s.Disability, e.PresentationId,
            p.ModuleCode, p.PresentationCode, p.LengthDays, e.PreviousAttempts, e.Credits, e.RegDay, e.UnregDay,
            e.Result, e.UpdatedAt, BitConverter.GetBytes(e.Version), assessments, others));
    }

    // ----------------------------------------------------------------- writes

    public Task<(int StudentId, int EnrollmentId)> CreateEnrollmentAsync(EnrollmentInput input, string changedBy, CancellationToken ct)
    {
        Calls++;
        Validate(input);
        if (_presentations.All(p => p.Id != input.PresentationId))
        {
            throw new InvalidInputException("Choose a course from the list.");
        }

        var studentId = _students.Keys.DefaultIfEmpty(0).Max() + 1;
        _students[studentId] = new Student(studentId, input.Gender, input.Region, input.HighestEducation, input.ImdBand, input.AgeBand, input.HasDisability);
        var e = new Enrollment
        {
            Id = _nextEnrollmentId++, StudentId = studentId, PresentationId = input.PresentationId,
            PreviousAttempts = input.PreviousAttempts, Credits = input.StudiedCredits,
            RegDay = input.RegistrationDay, UnregDay = input.UnregistrationDay, Result = input.FinalResult,
        };
        _enrollments.Add(e);
        Audit(changedBy, "Create", "Enrollment", e.Id.ToString(), $"New student {studentId}");
        return Task.FromResult((studentId, e.Id));
    }

    public Task UpdateEnrollmentAsync(int enrollmentId, byte[] rowVersion, EnrollmentInput input, string changedBy, CancellationToken ct)
    {
        Calls++;
        Validate(input);
        var e = _enrollments.FirstOrDefault(x => x.Id == enrollmentId) ?? throw new RecordNotFoundException("Enrollment not found.");
        if (BitConverter.ToInt64(rowVersion) != e.Version)
        {
            throw new ConflictException("Someone else changed this record after you opened it. Reload the page to see their changes.");
        }

        _students[e.StudentId] = new Student(e.StudentId, input.Gender, input.Region, input.HighestEducation, input.ImdBand, input.AgeBand, input.HasDisability);
        var old = e.Result;
        e.PreviousAttempts = input.PreviousAttempts;
        e.Credits = input.StudiedCredits;
        e.RegDay = input.RegistrationDay;
        e.UnregDay = input.UnregistrationDay;
        e.Result = input.FinalResult;
        e.UpdatedAt = DateTime.UtcNow;
        e.Version++;
        Audit(changedBy, "Update", "Enrollment", e.Id.ToString(), old != e.Result ? $"Result: {old ?? "in progress"} -> {e.Result ?? "in progress"}" : null);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteEnrollmentAsync(int enrollmentId, string changedBy, CancellationToken ct)
    {
        Calls++;
        var e = _enrollments.FirstOrDefault(x => x.Id == enrollmentId) ?? throw new RecordNotFoundException("Enrollment not found.");
        _results.RemoveAll(r => r.StudentId == e.StudentId && _assessments.Any(a => a.Id == r.AssessmentId && a.PresentationId == e.PresentationId));
        _enrollments.Remove(e);
        var studentDeleted = _enrollments.All(x => x.StudentId != e.StudentId);
        if (studentDeleted)
        {
            _results.RemoveAll(r => r.StudentId == e.StudentId);
            _students.Remove(e.StudentId);
        }
        Audit(changedBy, "Delete", "Enrollment", e.Id.ToString(), $"Student {e.StudentId} removed");
        return Task.FromResult(studentDeleted);
    }

    public Task SaveResultAsync(int enrollmentId, ScoreInput input, string changedBy, CancellationToken ct)
    {
        Calls++;
        if (input.Score is < 0 or > 100)
        {
            throw new InvalidInputException("Score must be between 0 and 100.");
        }
        var e = _enrollments.FirstOrDefault(x => x.Id == enrollmentId);
        if (e is null || !_assessments.Any(a => a.Id == input.AssessmentId && a.PresentationId == e.PresentationId))
        {
            throw new RecordNotFoundException("That assessment does not belong to this enrollment.");
        }

        var r = _results.FirstOrDefault(x => x.StudentId == e.StudentId && x.AssessmentId == input.AssessmentId);
        var existed = r is not null;
        if (r is null)
        {
            r = new Result { StudentId = e.StudentId, AssessmentId = input.AssessmentId };
            _results.Add(r);
        }
        r.Score = input.Score;
        r.SubmittedDay = input.SubmittedDay;
        r.IsBanked = input.IsBanked;
        Audit(changedBy, existed ? "Update" : "Create", "AssessmentResult", $"{e.StudentId}/{input.AssessmentId}", $"Score -> {input.Score}");
        return Task.CompletedTask;
    }

    public Task DeleteResultAsync(int enrollmentId, int assessmentId, string changedBy, CancellationToken ct)
    {
        Calls++;
        var e = _enrollments.FirstOrDefault(x => x.Id == enrollmentId);
        var removed = e is null ? 0 : _results.RemoveAll(r => r.StudentId == e.StudentId && r.AssessmentId == assessmentId);
        if (removed == 0)
        {
            throw new RecordNotFoundException("There is no result to delete for that assessment.");
        }
        Audit(changedBy, "Delete", "AssessmentResult", $"{e!.StudentId}/{assessmentId}", "Result removed");
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- reports

    public Task<Dashboard> GetDashboardAsync(CancellationToken ct)
    {
        Calls++;
        var finished = _enrollments.Where(e => e.Result is not null).ToList();
        decimal? Rate(Func<Enrollment, bool> f) => finished.Count == 0 ? null : Math.Round(100m * finished.Count(f) / finished.Count, 1);

        var totals = new DashboardTotals(_students.Count, _enrollments.Count, _presentations.Count, _results.Count,
            Rate(e => e.Result is "Pass" or "Distinction"), Rate(e => e.Result == "Withdrawn"));

        var courses = _presentations.OrderBy(p => p.ModuleCode).ThenBy(p => p.PresentationCode).Select(p =>
        {
            var es = _enrollments.Where(e => e.PresentationId == p.Id).ToList();
            var done = es.Where(e => e.Result is not null).ToList();
            return new CourseOutcome(p.Id, p.ModuleCode, p.PresentationCode, es.Count,
                es.Count(e => e.Result == "Distinction"), es.Count(e => e.Result == "Pass"), es.Count(e => e.Result == "Fail"),
                es.Count(e => e.Result == "Withdrawn"), es.Count(e => e.Result is null),
                done.Count == 0 ? null : Math.Round(100m * done.Count(e => e.Result is "Pass" or "Distinction") / done.Count, 1));
        }).ToList();

        return Task.FromResult(new Dashboard(totals, courses));
    }

    public Task<AtRiskReport> GetAtRiskAsync(AtRiskQuery q, CancellationToken ct)
    {
        Calls++;
        var signals = _enrollments
            .Where(e => q.PresentationId is null || e.PresentationId == q.PresentationId)
            .Where(e => e.UnregDay is null || e.UnregDay > q.CutoffDay)
            .Select(e =>
            {
                var due = _assessments.Where(a => a.PresentationId == e.PresentationId && a.Type != "Exam" && a.DueDay <= q.CutoffDay).ToList();
                var done = _results.Where(r => r.StudentId == e.StudentId && due.Any(a => a.Id == r.AssessmentId)).ToList();
                decimal? avg = done.Any(r => r.Score.HasValue) ? Math.Round(done.Where(r => r.Score.HasValue).Average(r => r.Score!.Value), 1) : null;
                var flagged = due.Count - done.Count >= q.MinMissed || avg < q.ScoreThreshold;
                return (e, due: due.Count, done: done.Count, avg, flagged);
            })
            .ToList();

        var rows = signals.Where(x => x.flagged)
            .OrderByDescending(x => x.due - x.done).ThenBy(x => x.avg).ThenBy(x => x.e.Id)
            .Take(AtRiskQuery.MaxRows)
            .Select(x => new AtRiskRow(x.e.Id, x.e.StudentId, Presentation(x.e.PresentationId).ModuleCode,
                Presentation(x.e.PresentationId).PresentationCode, x.due, x.done, x.due - x.done, x.avg, x.e.Result))
            .ToList();

        AtRiskGroup? Group(bool flagged)
        {
            var g = signals.Where(x => x.flagged == flagged).ToList();
            if (g.Count == 0)
            {
                return null;
            }
            var finished = g.Count(x => x.e.Result is not null);
            var bad = g.Count(x => x.e.Result is "Fail" or "Withdrawn");
            return new AtRiskGroup(flagged, g.Count, g.Count(x => x.e.Result is "Pass" or "Distinction"), bad,
                g.Count(x => x.e.Result is null), finished == 0 ? null : Math.Round(100m * bad / finished, 1));
        }

        return Task.FromResult(new AtRiskReport(rows, Group(true), Group(false)));
    }

    public Task<IReadOnlyList<AuditEntry>> GetAuditLogAsync(int top, CancellationToken ct)
    {
        Calls++;
        IReadOnlyList<AuditEntry> list = _audit.OrderByDescending(a => a.AuditId).Take(top).ToList();
        return Task.FromResult(list);
    }

    // ---------------------------------------------------------------- helpers

    private void Validate(EnrollmentInput i)
    {
        if (i.Gender is not ("F" or "M")) throw new InvalidInputException("Gender must be F or M.");
        if (_students.Values.All(s => s.Region != i.Region)) throw new InvalidInputException("Choose a region from the list.");
        if (_students.Values.All(s => s.Education != i.HighestEducation)) throw new InvalidInputException("Choose an education level from the list.");
        if (_students.Values.All(s => s.AgeBand != i.AgeBand)) throw new InvalidInputException("Choose an age band from the list.");
        if (i.StudiedCredits is < 1 or > 1000) throw new InvalidInputException("Studied credits must be between 1 and 1000.");
        if (i.RegistrationDay is not null && i.UnregistrationDay < i.RegistrationDay)
            throw new InvalidInputException("Unregistration day cannot be before the registration day.");
    }

    private IEnumerable<Result> ResultsFor(Enrollment e) =>
        _results.Where(r => r.StudentId == e.StudentId && _assessments.Any(a => a.Id == r.AssessmentId && a.PresentationId == e.PresentationId));

    private PresentationOption Presentation(int id) => _presentations.First(p => p.Id == id);

    private int PresentationId(string module, string code) =>
        _presentations.First(p => p.ModuleCode == module && p.PresentationCode == code).Id;

    private void Audit(string by, string action, string entity, string key, string? details) =>
        _audit.Add(new AuditEntry(_audit.Count + 1, DateTime.UtcNow, by, action, entity, key, details));

    private static int? ToInt(string value) => int.TryParse(value, out var n) ? n : null;

    /// <summary>Minimal CSV reader for the sample files (no commas inside values).</summary>
    private static List<string[]> Read(string folder, string file) =>
        File.ReadAllLines(Path.Combine(folder, file))
            .Skip(1)
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.TrimEnd('\r').Split(',').Select(v => v.Trim().Trim('"')).ToArray())
            .ToList();
}
