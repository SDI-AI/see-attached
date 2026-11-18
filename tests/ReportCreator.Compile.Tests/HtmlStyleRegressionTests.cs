using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReportCreator.Compile;
using ReportCreator.Model;

public class HtmlStyleRegressionTests
{
    [Fact]
    public void QuotedFontStacks_DoNotTruncateStyleAttributes()
    {
        BoxStyle Style()=>new() {FontFamily="Arial", Padding=new() {Left=1}, Background="#ffffff"};
        var report=new ReportDefinition {Body=new() {Blocks=[
            new TextBlock {Id="text",Text="One\nTwo",FontFamily="Arial",Style=Style()},
            new FieldBlock {Id="field",Path="value",FontFamily="Arial",Style=Style()},
            new RuleBlock {Id="rule",Style=Style()},
            new SpacerBlock {Id="spacer",Style=Style()},
            new PanelBlock {Id="panel",Style=Style()},
            new ColumnsBlock {Id="columns",Columns=[new Column()],Style=Style()},
            new ImageBlock {Id="image",Source="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",Style=Style()},
            new TableBlock {Id="table",DataPath="lines",Columns=[new() {Header="Value",Path="value"}],TableStyle=new() {FontFamily="Arial",FontSizePt=8},Style=Style()}
        ]}};
        var html=ReportCompiler.Compile(report,JsonSerializer.SerializeToElement(new {value="test",lines=new[]{new {value="row"}}}));
        foreach(var id in new[]{"text","field","rule","spacer","panel","columns","image","table"})
        {
            var tag=Regex.Match(html,"<[^>]+data-id=\""+id+"\"[^>]*>").Value;
            var style=WebUtility.HtmlDecode(Regex.Match(tag,"style=\"([^\"]*)\"").Groups[1].Value);
            Assert.Contains("font-family:Arial, \"Source Sans 3\", sans-serif;",style);
            Assert.Contains("padding:0mm 0mm 0mm 1mm;",style);
            Assert.Contains("background:#ffffff;",style);
            Assert.DoesNotContain("font-family:Arial, \"",tag);
        }
        var text=Regex.Match(html,"<div[^>]+data-id=\"text\"[^>]*>").Value;
        Assert.Contains("white-space:pre-wrap;",text);
        Assert.Contains("table.page-frame > tfoot > tr > td { font-weight: normal; }",html);
    }
}
