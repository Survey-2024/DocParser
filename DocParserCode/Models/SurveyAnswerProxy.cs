namespace DocParserCode.Models;

public partial class SurveyAnswerProxy
{
    public int SurveyQuestionId { get; set; }
    public string AnswerText { get; set; } = null!;
}
