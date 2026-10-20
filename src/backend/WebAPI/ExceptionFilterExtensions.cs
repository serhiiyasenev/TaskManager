using System.Net;
using BLL.Exceptions;

namespace WebAPI;

public static class ExceptionFilterExtensions
{
    public static HttpStatusCode ParseException(this Exception exception)
    {
        return exception switch
        {
            UnauthorizedAccessException _ => HttpStatusCode.Unauthorized,
            NotFoundException _ => HttpStatusCode.NotFound,
            CanNotDeleteException _ => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError
        };
    }
}
