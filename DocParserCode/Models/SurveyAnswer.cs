namespace DocParserCode.Models
{
    public class SurveyAnswer
    {
        public int SurveyAnswerId { get; set; }
        public int SurveyQuestionId { get; set; }
        public int SurveyId { get; set; }
        public string AnswerText { get; set; }
    }
}
