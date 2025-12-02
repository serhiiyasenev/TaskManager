using System.Net;
using BLL.Exceptions;

namespace WebAPI;

public static class ExceptionFilterExtensions
{
    public static HttpStatusCode ParseException(this Exception exception)
    {
        return exception switch
        {
            NotFoundException _ => HttpStatusCode.NotFound,
            CanNotDeleteException _ => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError
        };
    }
}