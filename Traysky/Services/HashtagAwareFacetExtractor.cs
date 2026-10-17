using idunno.Bluesky.RichText;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Traysky.Services;

/// <summary>
/// Wraps the library's extractor for links and mentions but swaps its hashtag facets for ones
/// from <see cref="HashtagFacetPolicy"/>, so a hyphenated tag like "#tray-tober" is one tag.
/// </summary>
public sealed class HashtagAwareFacetExtractor(IFacetExtractor inner) : IFacetExtractor
{
    public async Task<IList<Facet>> ExtractFacets(string text, CancellationToken cancellationToken)
    {
        IList<Facet> facets = await inner.ExtractFacets(text, cancellationToken) ?? [];

        List<Facet> result = [.. facets.Where(f => f.Features is null || !f.Features.Any(feature => feature is TagFacetFeature))];
        foreach (HashtagSpan span in HashtagFacetPolicy.Find(text))
            result.Add(new Facet(new ByteSlice(span.ByteStart, span.ByteEnd), [new TagFacetFeature(span.Tag)]));

        return result;
    }
}
