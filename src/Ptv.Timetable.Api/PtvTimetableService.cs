﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Ptv.Timetable.Api.Requests;
using Ptv.Timetable.Api.Responses;

namespace Ptv.Timetable.Api
{
    public sealed class PtvTimetableService : IPtvTimetableService
    {
        private static readonly Uri BaseUri = new Uri(PtvConstants.BaseUrl);

        private readonly string _developerId;
        private readonly string _securityKey;
        private readonly HttpClient _httpClient;

        public PtvTimetableService(string developerId, string securityKey)
        {
            _developerId = developerId;
            _securityKey = securityKey;

            //set up the handler
            var handler = new HttpClientHandler();
            if (handler.SupportsAutomaticDecompression)
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            //create a http client
            _httpClient = new HttpClient(handler);
        }

        public Task<HealthCheckResponse> PerformHealthCheckAsync()
        {
            return GetApiResponseAsync<HealthCheckResponse>(new HealthCheckRequest());
        }

        public Task<StopsNearbyResponse> GetStopsNearbyAsync(double latitude, double longitude)
        {
            return GetApiResponseAsync<StopsNearbyResponse>(new StopsNearbyRequest(latitude, longitude));
        }

        public Task<PointsOfInterestResponse> GetPointsOfInterestAsync(
            IEnumerable<PointOfInterestType> filterTypes,
            double topLeftLatitude,
            double topLeftLogitude,
            double bottomRightLatitude,
            double bottomRightLongitude,
            int gridDepth,
            int limit)
        {
            return GetApiResponseAsync<PointsOfInterestResponse>(new PointsOfInterestRequest(filterTypes, topLeftLatitude, topLeftLogitude, bottomRightLatitude, bottomRightLongitude, gridDepth, limit));
        }

        public Task<SearchResponse> PerformSearchAsync(string searchTerm)
        {
            return GetApiResponseAsync<SearchResponse>(new SearchRequest(searchTerm));
        }

        public Task<NextDeparturesResponse> ListBroadNextDeparturesAsync(TransportType transportType, int stopId, int limit)
        {
            return GetApiResponseAsync<NextDeparturesResponse>(new BroadNextDeparturesRequest(transportType, stopId, limit));
        }

        public Task<LinesByModeResponse> ListLinesByModeAsync(TransportType transportType)
        {
            return ListLinesByModeAsync(transportType, null);
        }

        public Task<LinesByModeResponse> ListLinesByModeAsync(TransportType transportType, string nameFilter)
        {
            return GetApiResponseAsync<LinesByModeResponse>(new LinesByModeRequest(transportType, nameFilter));
        }

        public Task<DisruptionsResponse> ListDisruptionsAsync(params DisruptionMode[] disruptionModes)
        {
            if (disruptionModes == null) throw new ArgumentNullException("disruptionModes");

            return GetApiResponseAsync<DisruptionsResponse>(new DisruptionsRequest(disruptionModes));
        }

        public Task<NextDeparturesResponse> ListSpecificNextDeparturesAsync(TransportType transportType, int lineId, int stopId, int directionId, int limit, DateTime? specifiedUtcTime = null)
        {
            return GetApiResponseAsync<NextDeparturesResponse>(new SpecificNextDeparturesRequest(transportType, lineId, stopId, directionId, limit, specifiedUtcTime));
        }

        //this is the GTFS input
        //public Task<NextDeparturesResponse> ListSpecificNextDepaturesAsync()
        //{
            
        //}

        public Task<StoppingPatternResponse> GetStoppingPatternAsync(TransportType transportType, int runId, int stopId, DateTime requestUtcTime)
        {
            return GetApiResponseAsync<StoppingPatternResponse>(new StoppingPatternRequest(transportType, runId, stopId, requestUtcTime));
        }

        public Task<StopsForLineResponse> ListStopsForLineAsync(TransportType transportType, int lineId)
        {
            return GetApiResponseAsync<StopsForLineResponse>(new StopsForLineRequest(transportType, lineId));
        }

        public async Task<byte[]> GetLineMapAsync(int lineId)
        {
            var uri = new Uri(string.Format(CultureInfo.InvariantCulture, PtvConstants.BaseMapUrl, lineId), UriKind.Absolute);

            var response = await _httpClient.GetAsync(uri);

            response.EnsureSuccessStatusCode();

            var htmlDocument = new HtmlDocument {OptionFixNestedTags = true};

            var htmlBytes = await response.Content.ReadAsByteArrayAsync();
            var htmlStr = Encoding.UTF8.GetString(htmlBytes, 0, htmlBytes.Length - 1);

            htmlDocument.LoadHtml(htmlStr);

            var routeMapNode = htmlDocument.DocumentNode.Descendants().FirstOrDefault(node => node.Id == "route-map");
            var mapUrl = routeMapNode.Attributes["src"].Value;

            var mapUri = new Uri(mapUrl);

            return await _httpClient.GetByteArrayAsync(mapUri);
        }

        #region Helper Methods

        private async Task<TResponse> GetApiResponseAsync<TResponse>(IRequest request)
        {
            //build the raw request URL
            var requestUrl = request.BuildRequestUrl();

            //sign the request url
            var signedUrl = SignRequestUrl(requestUrl);

            //build the full request
            var uri = new Uri(BaseUri + signedUrl, UriKind.Absolute);

            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.GetAsync(uri);

                //if this is not a success, throw an exception
                response.EnsureSuccessStatusCode();

                //get the response string (JSON)
                string responseString = await response.Content.ReadAsStringAsync();

                //deserialize the response string into the response obkect
                return await Task.Run(() => JsonConvert.DeserializeObject<TResponse>(responseString));
            }
            catch (HttpRequestException ex)
            {
                throw new PtvTimetableException("An exception occurred querying the PTV API", ex)
                      {
                          ReponseStatusCode = response != null ? response.StatusCode : new HttpStatusCode(),
                          ResponseReasonPhrase = response != null ? response.ReasonPhrase : null,
                          RequestUri = uri
                      };
            }
        }

        private string SignRequestUrl(string requestUrl)
        {
            //if the request URL already contains a ''?', then we just want to append the devId to the existing query string
            //otherwise, start a new quest string
            char queryChar = requestUrl.Contains("?") ? '&' : '?';

            string url = string.Format(CultureInfo.InvariantCulture, "{0}{1}devid={2}", requestUrl, queryChar, _developerId);

            var signature = RequestSigner.ComputeHash(_securityKey, url);

            //append the signature to the url
            return string.Format(CultureInfo.InvariantCulture, "{0}&signature={1}", url, signature);
        }

        #endregion
    }
}