using System.Collections.Generic;

namespace DocParserCode.Models
{
    public class SurveyAnswerSubmit
    {
        public int SurveyTypeId { get; set; }
        public List<SurveyAnswerProxy> SurveyAnswers { get; set; }
    }
}
