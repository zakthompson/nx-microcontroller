interface GameCardProps {
  title: string;
  subtitle?: string;
  href?: string;
  gradient: string;
  showIcon?: boolean;
  coverArt?: string;
  comingSoon?: boolean;
}

export default function GameCard({
  title,
  subtitle,
  href,
  gradient,
  showIcon,
  coverArt,
  comingSoon = false,
}: GameCardProps) {
  const CardWrapper = href && !comingSoon ? 'a' : 'div';
  const cardProps = href && !comingSoon ? { href } : {};

  return (
    <CardWrapper
      {...cardProps}
      className={`group relative ${comingSoon ? 'cursor-default' : ''}`}
    >
      <div
        className={`relative aspect-[3/4] overflow-hidden rounded-xl shadow-lg ${!comingSoon ? 'transition-all duration-300 hover:scale-105 hover:shadow-2xl' : ''}`}
      >
        <div
          className={`aspect-[3/4] ${gradient} flex items-center justify-center p-6 text-white`}
        >
          {coverArt ? (
            <img
              src={coverArt}
              alt={title}
              className="h-full w-full object-cover"
            />
          ) : showIcon ? (
            <svg
              xmlns="http://www.w3.org/2000/svg"
              fill="none"
              viewBox="0 0 24 24"
              strokeWidth={1.5}
              stroke="currentColor"
              className="h-16 w-16"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M9.594 3.94c.09-.542.56-.94 1.11-.94h2.593c.55 0 1.02.398 1.11.94l.213 1.281c.063.374.313.686.645.87.074.04.147.083.22.127.325.196.72.257 1.075.124l1.217-.456a1.125 1.125 0 0 1 1.37.49l1.296 2.247a1.125 1.125 0 0 1-.26 1.431l-1.003.827c-.293.241-.438.613-.43.992a7.723 7.723 0 0 1 0 .255c-.008.378.137.75.43.991l1.004.827c.424.35.534.955.26 1.43l-1.298 2.247a1.125 1.125 0 0 1-1.369.491l-1.217-.456c-.355-.133-.75-.072-1.076.124a6.47 6.47 0 0 1-.22.128c-.331.183-.581.495-.644.869l-.213 1.281c-.09.543-.56.94-1.11.94h-2.594c-.55 0-1.019-.398-1.11-.94l-.213-1.281c-.062-.374-.312-.686-.644-.87a6.52 6.52 0 0 1-.22-.127c-.325-.196-.72-.257-1.076-.124l-1.217.456a1.125 1.125 0 0 1-1.369-.49l-1.297-2.247a1.125 1.125 0 0 1 .26-1.431l1.004-.827c.292-.24.437-.613.43-.991a6.932 6.932 0 0 1 0-.255c.007-.38-.138-.751-.43-.992l-1.004-.827a1.125 1.125 0 0 1-.26-1.43l1.297-2.247a1.125 1.125 0 0 1 1.37-.491l1.216.456c.356.133.751.072 1.076-.124.072-.044.146-.086.22-.128.332-.183.582-.495.644-.869l.214-1.28Z"
              />
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z"
              />
            </svg>
          ) : null}
        </div>

        {comingSoon && (
          <div className="absolute inset-0 flex items-center justify-center bg-black/60 backdrop-blur-sm">
            <span className="text-3xl font-bold text-white drop-shadow-lg">
              Coming Soon
            </span>
          </div>
        )}

        {!comingSoon && (
          <div className="absolute inset-0 bg-black opacity-0 transition-opacity duration-300 group-hover:opacity-10" />
        )}
      </div>

      <div className="mt-3 text-center">
        <h3 className="text-xl font-bold text-white drop-shadow-lg">
          {title}
        </h3>
        {subtitle && (
          <p className="mt-1 text-sm text-gray-400 drop-shadow">{subtitle}</p>
        )}
      </div>
    </CardWrapper>
  );
}
